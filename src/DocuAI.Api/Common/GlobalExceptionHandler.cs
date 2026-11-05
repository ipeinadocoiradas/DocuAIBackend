using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace DocuAI.Api.Common;

/// <summary>
/// Manejador global (<see cref="IExceptionHandler"/>) para fallos inesperados de
/// infraestructura o red. Los errores de negocio nunca llegan aquí: viajan como Result.
/// Traduce cada excepción a ProblemDetails sin filtrar detalles internos fuera de desarrollo.
/// </summary>
internal sealed partial class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    IHostEnvironment environment,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    /// <summary>Código no estándar (nginx) para peticiones canceladas por el cliente.</summary>
    private const int ClientClosedRequest = 499;

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (statusCode, title) = Classify(exception, httpContext);

        if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            LogServerError(logger, exception, statusCode, httpContext.Request.Method, httpContext.Request.Path.Value);
        }
        else
        {
            LogClientError(logger, exception.GetType().Name, statusCode, httpContext.Request.Method, httpContext.Request.Path.Value);
        }

        httpContext.Response.StatusCode = statusCode;

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = environment.IsDevelopment()
                ? exception.Message
                : "Se ha producido un error inesperado. Si persiste, contacte con soporte indicando el traceId.",
            Instance = $"{httpContext.Request.Method} {httpContext.Request.Path}"
        };

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problemDetails
        });
    }

    private static (int StatusCode, string Title) Classify(Exception exception, HttpContext httpContext) => exception switch
    {
        OperationCanceledException when httpContext.RequestAborted.IsCancellationRequested =>
            (ClientClosedRequest, "Petición cancelada por el cliente"),
        BadHttpRequestException badRequest =>
            (badRequest.StatusCode, "Petición HTTP no válida"),
        BrokenCircuitException =>
            (StatusCodes.Status503ServiceUnavailable, "Proveedor de IA temporalmente no disponible (circuito abierto)"),
        TimeoutRejectedException =>
            (StatusCodes.Status504GatewayTimeout, "Tiempo de espera agotado con el proveedor de IA"),
        HttpRequestException =>
            (StatusCodes.Status502BadGateway, "Error de comunicación con un servicio externo"),
        DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } } =>
            (StatusCodes.Status409Conflict, "El recurso ya existe"),
        DbUpdateConcurrencyException =>
            (StatusCodes.Status409Conflict, "Conflicto de concurrencia"),
        NpgsqlException or TimeoutException =>
            (StatusCodes.Status503ServiceUnavailable, "Base de datos no disponible"),
        _ =>
            (StatusCodes.Status500InternalServerError, "Error interno del servidor")
    };

    [LoggerMessage(Level = LogLevel.Error, Message = "Excepción no controlada ({StatusCode}) en {Method} {Path}")]
    private static partial void LogServerError(ILogger logger, Exception exception, int statusCode, string method, string? path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Excepción {ExceptionType} traducida a {StatusCode} en {Method} {Path}")]
    private static partial void LogClientError(ILogger logger, string exceptionType, int statusCode, string method, string? path);
}
