using System.Diagnostics;
using DocuAI.Domain.Common;
using MediatR;
using Microsoft.Extensions.Logging;

namespace DocuAI.Application.Behaviors;

/// <summary>
/// Registra la ejecución de cada caso de uso, su duración y el código de error
/// cuando el resultado es fallido.
/// </summary>
internal sealed class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        var startTimestamp = Stopwatch.GetTimestamp();

        BehaviorLog.Handling(logger, requestName);

        var response = await next();
        var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

        if (response.IsFailure)
        {
            BehaviorLog.Failed(logger, requestName, response.Error.Code, response.Error.Type, elapsedMs);
        }
        else
        {
            BehaviorLog.Handled(logger, requestName, elapsedMs);
        }

        return response;
    }
}

/// <summary>Mensajes de log de alto rendimiento (generados en compilación).</summary>
internal static partial class BehaviorLog
{
    [LoggerMessage(Level = LogLevel.Debug, Message = "Procesando {RequestName}")]
    public static partial void Handling(ILogger logger, string requestName);

    [LoggerMessage(Level = LogLevel.Information, Message = "{RequestName} completado en {ElapsedMs:0.0} ms")]
    public static partial void Handled(ILogger logger, string requestName, double elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{RequestName} finalizó con error {ErrorCode} ({ErrorType}) en {ElapsedMs:0.0} ms")]
    public static partial void Failed(ILogger logger, string requestName, string errorCode, ErrorType errorType, double elapsedMs);
}
