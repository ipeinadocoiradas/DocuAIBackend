using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace DocuAI.Api.Common;

/// <summary>Evento SSE con un fragmento de texto generado.</summary>
internal sealed record TokenEvent(string Text);

/// <summary>Evento SSE de error producido durante el streaming.</summary>
internal sealed record StreamErrorEvent(string Code, string Message);

/// <summary>Evento SSE de finalización.</summary>
internal sealed record StreamCompletedEvent(int TokenCount);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(TokenEvent))]
[JsonSerializable(typeof(StreamErrorEvent))]
[JsonSerializable(typeof(StreamCompletedEvent))]
internal sealed partial class SseJsonContext : JsonSerializerContext
{
}

/// <summary>
/// <see cref="IActionResult"/> que transmite una secuencia de tokens como Server-Sent Events
/// (<c>text/event-stream</c>) usando <see cref="SseFormatter"/>.
/// <para>
/// Eventos emitidos: <c>token</c> ({"text":"…"}), <c>error</c> ({"code","message"}) y
/// <c>done</c> ({"tokenCount":n}). Como las cabeceras ya se han enviado, un fallo durante
/// el streaming se comunica con un evento <c>error</c> en lugar de un código HTTP.
/// </para>
/// </summary>
internal sealed partial class ServerSentEventsResult(IAsyncEnumerable<string> tokens, ILogger logger) : IActionResult
{
    public const string TokenEventType = "token";
    public const string ErrorEventType = "error";
    public const string DoneEventType = "done";

    public async Task ExecuteResultAsync(ActionContext context)
    {
        var httpContext = context.HttpContext;
        var response = httpContext.Response;
        var cancellationToken = httpContext.RequestAborted;

        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "text/event-stream; charset=utf-8";
        response.Headers.CacheControl = "no-cache, no-store";
        response.Headers["X-Accel-Buffering"] = "no"; // Evita el buffering en proxies inversos (nginx).

        httpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        await response.StartAsync(cancellationToken);

        try
        {
            await SseFormatter.WriteAsync(BuildEventsAsync(cancellationToken), response.Body, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // El cliente cerró la conexión: no hay nada más que enviar.
            LogClientDisconnected(logger, httpContext.TraceIdentifier);
        }
    }

    private async IAsyncEnumerable<SseItem<string>> BuildEventsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var tokenCount = 0;
        await using var enumerator = tokens.GetAsyncEnumerator(cancellationToken);

        while (true)
        {
            bool hasNext;
            SseItem<string>? errorEvent = null;

            // No se puede usar 'yield' dentro de un try con catch: se captura el fallo y se emite después.
            try
            {
                hasNext = await enumerator.MoveNextAsync();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                yield break;
            }
            catch (Exception exception)
            {
                LogStreamingFailed(logger, exception);
                hasNext = false;
                errorEvent = new SseItem<string>(
                    JsonSerializer.Serialize(
                        new StreamErrorEvent("Chat.StreamingFailed", "Se ha producido un error al generar la respuesta. Inténtelo de nuevo."),
                        SseJsonContext.Default.StreamErrorEvent),
                    ErrorEventType);
            }

            if (errorEvent is { } error)
            {
                yield return error;
                yield break;
            }

            if (!hasNext)
            {
                break;
            }

            tokenCount++;
            yield return new SseItem<string>(
                JsonSerializer.Serialize(new TokenEvent(enumerator.Current), SseJsonContext.Default.TokenEvent),
                TokenEventType);
        }

        yield return new SseItem<string>(
            JsonSerializer.Serialize(new StreamCompletedEvent(tokenCount), SseJsonContext.Default.StreamCompletedEvent),
            DoneEventType);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Error durante el streaming de la respuesta del agente")]
    private static partial void LogStreamingFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "El cliente cerró la conexión SSE ({TraceId})")]
    private static partial void LogClientDisconnected(ILogger logger, string traceId);
}
