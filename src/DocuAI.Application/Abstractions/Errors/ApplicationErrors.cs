using DocuAI.Domain.Common;

namespace DocuAI.Application.Abstractions.Errors;

/// <summary>Errores relativos a los proveedores de IA (embeddings y LLM).</summary>
public static class AiErrors
{
    public static readonly Error RateLimited = Error.ExternalService(
        "Ai.RateLimited",
        "El proveedor de IA ha rechazado la petición por exceso de cuota (HTTP 429) tras agotar los reintentos.");

    public static readonly Error Unauthorized = Error.ExternalService(
        "Ai.Unauthorized",
        "Las credenciales del proveedor de IA no son válidas.");

    public static readonly Error Unavailable = Error.ExternalService(
        "Ai.Unavailable",
        "El proveedor de IA no está disponible temporalmente.");

    public static readonly Error InvalidResponse = Error.ExternalService(
        "Ai.InvalidResponse",
        "El proveedor de IA ha devuelto una respuesta no válida.");

    public static Error ProviderError(int statusCode, string? detail = null) => Error.ExternalService(
        "Ai.ProviderError",
        $"El proveedor de IA ha respondido con el código {statusCode}.{(string.IsNullOrWhiteSpace(detail) ? string.Empty : $" {detail}")}");

    public static Error ModelNotAvailable(string model) => Error.ExternalService(
        "Ai.ModelNotAvailable",
        $"El modelo '{model}' no está disponible en el proveedor (en Ollama, descárguelo con 'ollama pull {model}').");

    public static Error EmbeddingCountMismatch(int expected, int actual) => Error.ExternalService(
        "Ai.EmbeddingCountMismatch",
        $"Se esperaban {expected} embeddings y se recibieron {actual}.");

    public static Error EmbeddingDimensionMismatch(int expected, int actual) => Error.ExternalService(
        "Ai.EmbeddingDimensionMismatch",
        $"El embedding tiene {actual} dimensiones y la base vectorial espera {expected}.");
}

/// <summary>Errores del caso de uso de chat.</summary>
public static class ChatErrors
{
    public static readonly Error KnowledgeBaseEmpty = Error.BusinessRule(
        "Chat.KnowledgeBaseEmpty",
        "La base de conocimiento no contiene todavía documentos indexados.");
}

/// <summary>Errores de autenticación.</summary>
public static class AuthErrors
{
    public static readonly Error InvalidCredentials = Error.Unauthorized(
        "Auth.InvalidCredentials",
        "Usuario o contraseña incorrectos.");

    public static readonly Error IssuerDisabled = Error.Forbidden(
        "Auth.IssuerDisabled",
        "La emisión local de tokens está deshabilitada en este entorno. Utilice el proveedor de identidad corporativo.");

    public static readonly Error UserNotIdentified = Error.Unauthorized(
        "Auth.UserNotIdentified",
        "No se ha podido identificar al usuario de la petición.");
}
