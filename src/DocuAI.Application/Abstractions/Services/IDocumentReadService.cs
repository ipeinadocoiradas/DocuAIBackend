using DocuAI.Domain.Documents;

namespace DocuAI.Application.Abstractions.Services;

/// <summary>Resumen de documento para listados.</summary>
public sealed record DocumentSummaryResponse(
    Guid Id,
    string FileName,
    string Status,
    long SizeInBytes,
    int ChunkCount,
    string UploadedBy,
    DateTimeOffset UploadedAtUtc,
    DateTimeOffset? ProcessedAtUtc);

/// <summary>Detalle completo del estado de ingesta de un documento.</summary>
public sealed record DocumentDetailsResponse(
    Guid Id,
    string FileName,
    string ContentType,
    string Status,
    long SizeInBytes,
    int ChunkCount,
    int ProcessingAttempts,
    string? FailureReason,
    string UploadedBy,
    DateTimeOffset UploadedAtUtc,
    DateTimeOffset? ProcessingStartedAtUtc,
    DateTimeOffset? ProcessedAtUtc);

/// <summary>Página de resultados.</summary>
public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public bool HasPreviousPage => Page > 1;

    public bool HasNextPage => Page < TotalPages;
}

/// <summary>Campo por el que se ordena el listado de documentos.</summary>
public enum DocumentSortField
{
    /// <summary>Fecha de subida (por defecto).</summary>
    UploadedAt = 0,

    /// <summary>Fecha de fin de la ingesta. Los documentos sin procesar quedan siempre al final.</summary>
    ProcessedAt = 1,

    /// <summary>Nombre del fichero.</summary>
    FileName = 2,

    /// <summary>Tamaño del fichero.</summary>
    Size = 3
}

/// <summary>Sentido de la ordenación.</summary>
public enum SortDirection
{
    Asc = 0,
    Desc = 1
}

/// <summary>
/// Criterios de búsqueda, filtrado, ordenación y paginación del listado de documentos.
/// Los valores llegan ya validados desde el caso de uso.
/// </summary>
public sealed record DocumentListCriteria
{
    /// <summary>Página solicitada (base 1).</summary>
    public required int Page { get; init; }

    /// <summary>Elementos por página.</summary>
    public required int PageSize { get; init; }

    /// <summary>Texto a buscar en el nombre del fichero (contiene, sin distinguir mayúsculas). <c>null</c> = sin filtro.</summary>
    public string? Search { get; init; }

    /// <summary>Estados admitidos. Vacío = todos.</summary>
    public IReadOnlyCollection<DocumentStatus> Statuses { get; init; } = [];

    /// <summary>Límite inferior <b>inclusivo</b> de la fecha de subida.</summary>
    public DateTimeOffset? UploadedFrom { get; init; }

    /// <summary>Límite superior <b>exclusivo</b> de la fecha de subida (intervalo semiabierto).</summary>
    public DateTimeOffset? UploadedTo { get; init; }

    public DocumentSortField SortBy { get; init; } = DocumentSortField.UploadedAt;

    public SortDirection SortDirection { get; init; } = SortDirection.Desc;
}

/// <summary>
/// Lado de lectura (CQRS) de documentos: proyecciones optimizadas sin cargar el agregado.
/// </summary>
public interface IDocumentReadService
{
    Task<DocumentDetailsResponse?> GetDetailsAsync(Guid documentId, CancellationToken cancellationToken = default);

    Task<PagedResponse<DocumentSummaryResponse>> GetPageAsync(DocumentListCriteria criteria, CancellationToken cancellationToken = default);
}
