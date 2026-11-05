using System.ComponentModel.DataAnnotations;
using DocuAI.Application.Abstractions.Services;
using DocuAI.Application.Features.Documents.ListDocuments;
using DocuAI.Domain.Documents;
using Microsoft.AspNetCore.Mvc;

namespace DocuAI.Api.Contracts;

/// <summary>
/// Parámetros de query string del listado de documentos.
/// Ejemplo: <c>?search=manual&amp;status=Pending&amp;status=Failed&amp;sortBy=FileName&amp;sortDirection=Asc&amp;page=1&amp;pageSize=20</c>.
/// </summary>
/// <remarks>
/// Los enumerados se admiten por nombre sin distinguir mayúsculas. Un valor no reconocido
/// produce un 400 con <c>ValidationProblemDetails</c>.
/// </remarks>
public sealed record ListDocumentsRequest
{
    /// <summary>Página (base 1).</summary>
    public int Page { get; init; } = 1;

    /// <summary>Elementos por página (1-100).</summary>
    public int PageSize { get; init; } = ListDocumentsQuery.DefaultPageSize;

    /// <summary>Texto contenido en el nombre del fichero (sin distinguir mayúsculas).</summary>
    public string? Search { get; init; }

    /// <summary>Estados a incluir; se puede repetir: <c>status=Pending&amp;status=Processing</c>.</summary>
    [FromQuery(Name = "status")]
    public DocumentStatus[]? Status { get; init; }

    /// <summary>Fecha de subida mínima, inclusiva (ISO 8601 con zona, p. ej. <c>2026-10-01T00:00:00+02:00</c>).</summary>
    public DateTimeOffset? UploadedFrom { get; init; }

    /// <summary>Fecha de subida máxima, <b>exclusiva</b> (para incluir el día 31, envíe el día 1 siguiente a las 00:00).</summary>
    public DateTimeOffset? UploadedTo { get; init; }

    /// <summary>Campo de ordenación: <c>UploadedAt</c> (por defecto), <c>ProcessedAt</c>, <c>FileName</c> o <c>Size</c>.</summary>
    public DocumentSortField SortBy { get; init; } = DocumentSortField.UploadedAt;

    /// <summary>Sentido: <c>Desc</c> (por defecto) o <c>Asc</c>.</summary>
    public SortDirection SortDirection { get; init; } = SortDirection.Desc;

    /// <summary>Traduce la petición HTTP al caso de uso.</summary>
    public ListDocumentsQuery ToQuery() => new(
        Page,
        PageSize,
        Search,
        Status,
        UploadedFrom,
        UploadedTo,
        SortBy,
        SortDirection);
}

/// <summary>Petición de consulta al chat RAG.</summary>
public sealed record AskQuestionRequest
{
    /// <summary>Pregunta en lenguaje natural.</summary>
    [Required]
    public required string Question { get; init; }

    /// <summary>Fragmentos de contexto a recuperar (opcional, por defecto el configurado).</summary>
    public int? TopK { get; init; }
}

/// <summary>Credenciales para el emisor local de tokens.</summary>
public sealed record IssueTokenRequest
{
    [Required]
    public required string UserName { get; init; }

    [Required]
    public required string Password { get; init; }
}
