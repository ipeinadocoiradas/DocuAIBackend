using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DocuAI.Application.Abstractions.Errors;
using DocuAI.Application.Abstractions.Services;
using DocuAI.Domain.Common;
using DocuAI.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace DocuAI.Infrastructure.AI.Embeddings;

/// <summary>
/// Cliente tipado del endpoint <c>POST {BaseUrl}embeddings</c> con formato OpenAI.
/// Funciona con Ollama (<c>/v1/embeddings</c>, por defecto), OpenRouter u OpenAI.
/// Los reintentos y timeouts los aplica el pipeline de resiliencia del <see cref="HttpClient"/>;
/// aquí solo se traduce el resultado final a <see cref="Result"/>, sin propagar excepciones de red.
/// </summary>
internal sealed partial class OpenAiCompatibleEmbeddingService(
    HttpClient httpClient,
    IOptions<EmbeddingOptions> embeddingOptions,
    ILogger<OpenAiCompatibleEmbeddingService> logger) : IEmbeddingService
{
    private const int MaxLoggedBodyLength = 500;

    private readonly EmbeddingOptions _options = embeddingOptions.Value;

    public async Task<Result<IReadOnlyList<float[]>>> GenerateAsync(
        IReadOnlyList<string> inputs,
        CancellationToken cancellationToken = default)
    {
        if (inputs.Count == 0)
        {
            return Result.Success<IReadOnlyList<float[]>>([]);
        }

        var request = new EmbeddingRequest(
            _options.Model,
            inputs,
            _options.SendDimensionsParameter ? _options.Dimensions : null);

        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                "embeddings",
                request,
                EmbeddingJsonContext.Default.EmbeddingRequest,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return await MapErrorResponseAsync(response, cancellationToken);
            }

            var payload = await response.Content.ReadFromJsonAsync(
                EmbeddingJsonContext.Default.EmbeddingResponse,
                cancellationToken);

            return MapPayload(payload, inputs.Count);
        }
        catch (Exception exception) when (exception is HttpRequestException or TimeoutRejectedException or BrokenCircuitException)
        {
            // Proveedor inaccesible (p. ej. Ollama detenido) tras agotar la estrategia de resiliencia.
            LogTransportFailure(logger, exception, _options.Model, _options.BaseUrl);
            return AiErrors.Unavailable;
        }
        catch (JsonException exception)
        {
            LogInvalidPayload(logger, exception, _options.Model);
            return AiErrors.InvalidResponse;
        }
    }

    private Result<IReadOnlyList<float[]>> MapPayload(EmbeddingResponse? payload, int expectedCount)
    {
        // Algunos proveedores devuelven HTTP 200 con un objeto "error" en el cuerpo.
        if (payload?.Error is { } providerError)
        {
            LogProviderError(logger, providerError.Code ?? 0, providerError.Message ?? string.Empty);
            return providerError.Code == (int)HttpStatusCode.TooManyRequests
                ? AiErrors.RateLimited
                : AiErrors.ProviderError(providerError.Code ?? 0, providerError.Message);
        }

        if (payload?.Data is not { Count: > 0 } data)
        {
            return AiErrors.InvalidResponse;
        }

        if (data.Count != expectedCount)
        {
            return AiErrors.EmbeddingCountMismatch(expectedCount, data.Count);
        }

        var embeddings = data
            .OrderBy(item => item.Index)
            .Select(item => item.Embedding)
            .ToArray();

        foreach (var embedding in embeddings)
        {
            if (embedding.Length != _options.Dimensions)
            {
                return AiErrors.EmbeddingDimensionMismatch(_options.Dimensions, embedding.Length);
            }
        }

        return Result.Success<IReadOnlyList<float[]>>(embeddings);
    }

    private async Task<Result<IReadOnlyList<float[]>>> MapErrorResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var statusCode = (int)response.StatusCode;

        LogProviderError(logger, statusCode, body.Length > MaxLoggedBodyLength ? body[..MaxLoggedBodyLength] : body);

        return response.StatusCode switch
        {
            HttpStatusCode.TooManyRequests => AiErrors.RateLimited,
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.PaymentRequired => AiErrors.Unauthorized,

            // Ollama responde 404 cuando el modelo no se ha descargado (ollama pull).
            HttpStatusCode.NotFound => AiErrors.ModelNotAvailable(_options.Model),
            >= HttpStatusCode.InternalServerError => AiErrors.Unavailable,
            _ => AiErrors.ProviderError(statusCode)
        };
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Fallo de transporte al generar embeddings con el modelo {Model} en {BaseUrl}")]
    private static partial void LogTransportFailure(ILogger logger, Exception exception, string model, string baseUrl);

    [LoggerMessage(Level = LogLevel.Error, Message = "Respuesta de embeddings no válida del modelo {Model}")]
    private static partial void LogInvalidPayload(ILogger logger, Exception exception, string model);

    [LoggerMessage(Level = LogLevel.Warning, Message = "El proveedor de embeddings respondió con error {StatusCode}: {Detail}")]
    private static partial void LogProviderError(ILogger logger, int statusCode, string detail);
}
