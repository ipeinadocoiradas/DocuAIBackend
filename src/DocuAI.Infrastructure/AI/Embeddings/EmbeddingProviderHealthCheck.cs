using System.Net.Http.Json;
using System.Text.Json;
using DocuAI.Infrastructure.Options;
using DocuAI.Infrastructure.Resilience;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace DocuAI.Infrastructure.AI.Embeddings;

/// <summary>
/// Comprueba que el proveedor de embeddings responde y que el modelo configurado está
/// disponible (en Ollama: descargado con <c>ollama pull</c>). Forma parte de <c>/health/ready</c>.
/// </summary>
internal sealed class EmbeddingProviderHealthCheck(
    IHttpClientFactory httpClientFactory,
    IOptions<EmbeddingOptions> embeddingOptions) : IHealthCheck
{
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(5);

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var options = embeddingOptions.Value;
        var client = httpClientFactory.CreateClient(ProviderHttpClientExtensions.EmbeddingsHealthClientName);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CheckTimeout);

        try
        {
            var models = await client.GetFromJsonAsync(
                "models",
                EmbeddingJsonContext.Default.ModelListResponse,
                timeout.Token);

            var isAvailable = models?.Data?.Any(model => IsSameModel(model.Id, options.Model)) ?? false;

            return isAvailable
                ? HealthCheckResult.Healthy($"Modelo de embeddings '{options.Model}' disponible.")
                : HealthCheckResult.Degraded(
                    $"El proveedor responde pero el modelo '{options.Model}' no está disponible. En Ollama: 'ollama pull {options.Model}'.");
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or JsonException)
        {
            return HealthCheckResult.Unhealthy(
                $"No se puede contactar con el proveedor de embeddings en {options.BaseUrl}.",
                exception);
        }
    }

    /// <summary>
    /// Ollama lista los modelos con etiqueta (<c>bge-m3:latest</c>); se acepta el nombre
    /// configurado con o sin etiqueta.
    /// </summary>
    private static bool IsSameModel(string listedId, string configuredModel) =>
        string.Equals(listedId, configuredModel, StringComparison.OrdinalIgnoreCase)
        || (!configuredModel.Contains(':') &&
            listedId.StartsWith($"{configuredModel}:", StringComparison.OrdinalIgnoreCase));
}
