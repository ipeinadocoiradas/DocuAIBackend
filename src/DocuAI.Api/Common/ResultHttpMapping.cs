using DocuAI.Domain.Common;
using Microsoft.AspNetCore.Mvc;

namespace DocuAI.Api.Common;

/// <summary>
/// Traducción centralizada de <see cref="Error"/> a respuestas HTTP RFC 9457 (ProblemDetails).
/// </summary>
internal static class ResultHttpMapping
{
    /// <summary>Código de estado HTTP correspondiente a cada tipo de error de negocio.</summary>
    public static int ToStatusCode(this ErrorType errorType) => errorType switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.BusinessRule => StatusCodes.Status422UnprocessableEntity,
        ErrorType.ExternalService => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status500InternalServerError
    };

    private static string ToTitle(this ErrorType errorType) => errorType switch
    {
        ErrorType.Validation => "Error de validación",
        ErrorType.Unauthorized => "No autenticado",
        ErrorType.Forbidden => "Acceso denegado",
        ErrorType.NotFound => "Recurso no encontrado",
        ErrorType.Conflict => "Conflicto",
        ErrorType.BusinessRule => "Regla de negocio incumplida",
        ErrorType.ExternalService => "Servicio externo no disponible",
        _ => "Error en el servidor"
    };

    private static string ToTypeUri(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "https://tools.ietf.org/html/rfc9110#section-15.5.1",
        StatusCodes.Status401Unauthorized => "https://tools.ietf.org/html/rfc9110#section-15.5.2",
        StatusCodes.Status403Forbidden => "https://tools.ietf.org/html/rfc9110#section-15.5.4",
        StatusCodes.Status404NotFound => "https://tools.ietf.org/html/rfc9110#section-15.5.5",
        StatusCodes.Status409Conflict => "https://tools.ietf.org/html/rfc9110#section-15.5.10",
        StatusCodes.Status422UnprocessableEntity => "https://tools.ietf.org/html/rfc9110#section-15.5.21",
        StatusCodes.Status503ServiceUnavailable => "https://tools.ietf.org/html/rfc9110#section-15.6.4",
        _ => "https://tools.ietf.org/html/rfc9110#section-15.6.1"
    };

    extension(ControllerBase controller)
    {
        /// <summary>Convierte un error de negocio en un <see cref="IActionResult"/> ProblemDetails.</summary>
        public IActionResult HandleFailure(Error error)
        {
            var statusCode = error.Type.ToStatusCode();
            var httpContext = controller.HttpContext;

            ProblemDetails problem = error is { Type: ErrorType.Validation, ValidationErrors.Count: > 0 }
                ? new ValidationProblemDetails(error.ValidationErrors.ToDictionary())
                : new ProblemDetails();

            problem.Status = statusCode;
            problem.Title = error.Type.ToTitle();
            problem.Detail = error.Description;
            problem.Type = ToTypeUri(statusCode);
            problem.Instance = $"{httpContext.Request.Method} {httpContext.Request.Path}";
            problem.Extensions["errorCode"] = error.Code;
            problem.Extensions["traceId"] = httpContext.TraceIdentifier;

            return new ObjectResult(problem)
            {
                StatusCode = statusCode,
                ContentTypes = { "application/problem+json" }
            };
        }

        /// <summary>Respuesta 200 con el valor o ProblemDetails según el resultado.</summary>
        public IActionResult OkOrProblem<T>(Result<T> result) =>
            result.IsSuccess ? controller.Ok(result.Value) : controller.HandleFailure(result.Error);

        /// <summary>Respuesta 204 sin contenido o ProblemDetails según el resultado.</summary>
        public IActionResult NoContentOrProblem(Result result) =>
            result.IsSuccess ? controller.NoContent() : controller.HandleFailure(result.Error);
    }
}
