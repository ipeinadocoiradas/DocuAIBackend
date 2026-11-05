namespace DocuAI.Domain.Common;

/// <summary>
/// Clasifica la naturaleza de un error de negocio. La capa de presentación
/// la utiliza para traducir el error al código de estado HTTP adecuado.
/// </summary>
public enum ErrorType
{
    /// <summary>Fallo genérico no clasificado.</summary>
    Failure = 0,

    /// <summary>Los datos de entrada no cumplen las reglas de validación.</summary>
    Validation = 1,

    /// <summary>El recurso solicitado no existe.</summary>
    NotFound = 2,

    /// <summary>La operación entra en conflicto con el estado actual (p. ej. duplicados).</summary>
    Conflict = 3,

    /// <summary>Credenciales ausentes o inválidas.</summary>
    Unauthorized = 4,

    /// <summary>El usuario autenticado no tiene permiso para la operación.</summary>
    Forbidden = 5,

    /// <summary>Se ha violado una regla de negocio (operación bien formada pero no procesable).</summary>
    BusinessRule = 6,

    /// <summary>Un servicio externo (LLM, embeddings…) no está disponible o ha respondido con error.</summary>
    ExternalService = 7
}

/// <summary>
/// Representa un error de negocio inmutable y tipado.
/// </summary>
/// <param name="Code">Código estable y legible por máquinas (p. ej. <c>Document.NotFound</c>).</param>
/// <param name="Description">Descripción legible para personas.</param>
/// <param name="Type">Categoría del error.</param>
public sealed record Error(string Code, string Description, ErrorType Type)
{
    /// <summary>Ausencia de error. Se usa en los resultados satisfactorios.</summary>
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.Failure);

    /// <summary>
    /// Errores de validación agrupados por propiedad. Solo se informa cuando
    /// <see cref="Type"/> es <see cref="ErrorType.Validation"/>.
    /// </summary>
    public IReadOnlyDictionary<string, string[]>? ValidationErrors { get; init; }

    public static Error Failure(string code, string description) => new(code, description, ErrorType.Failure);

    public static Error NotFound(string code, string description) => new(code, description, ErrorType.NotFound);

    public static Error Conflict(string code, string description) => new(code, description, ErrorType.Conflict);

    public static Error Unauthorized(string code, string description) => new(code, description, ErrorType.Unauthorized);

    public static Error Forbidden(string code, string description) => new(code, description, ErrorType.Forbidden);

    public static Error BusinessRule(string code, string description) => new(code, description, ErrorType.BusinessRule);

    public static Error ExternalService(string code, string description) => new(code, description, ErrorType.ExternalService);

    /// <summary>Crea un error de validación con el detalle opcional por propiedad.</summary>
    public static Error Validation(
        string code,
        string description,
        IReadOnlyDictionary<string, string[]>? validationErrors = null) =>
        new(code, description, ErrorType.Validation) { ValidationErrors = validationErrors };
}
