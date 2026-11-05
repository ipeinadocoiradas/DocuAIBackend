using System.Linq.Expressions;
using System.Text;
using DocuAI.Application.Abstractions.Services;
using DocuAI.Domain.Documents;
using Microsoft.EntityFrameworkCore;

namespace DocuAI.Infrastructure.Persistence.Repositories;

/// <summary>
/// Lado de lectura: proyecciones sin seguimiento que nunca cargan el texto extraído
/// ni los vectores.
/// </summary>
internal sealed class DocumentReadService(ApplicationDbContext dbContext) : IDocumentReadService
{
    /// <summary>Carácter de escape para los comodines de <c>ILIKE</c>.</summary>
    private const char LikeEscapeCharacter = '\\';

    public async Task<DocumentDetailsResponse?> GetDetailsAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        var id = new DocumentId(documentId);

        return await dbContext.Documents
            .AsNoTracking()
            .Where(document => document.Id == id)
            .Select(document => new DocumentDetailsResponse(
                document.Id.Value,
                document.FileName.Value,
                document.ContentType,
                document.Status.ToString(),
                document.SizeInBytes,
                document.ChunkCount,
                document.ProcessingAttempts,
                document.FailureReason,
                document.UploadedBy,
                document.UploadedAtUtc,
                document.ProcessingStartedAtUtc,
                document.ProcessedAtUtc))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<PagedResponse<DocumentSummaryResponse>> GetPageAsync(
        DocumentListCriteria criteria,
        CancellationToken cancellationToken = default)
    {
        var query = ApplyFilters(criteria);

        var totalCount = await query.CountAsync(cancellationToken);

        // Página fuera de rango: se evita la segunda consulta y se devuelve vacía con el total real.
        if (totalCount == 0 || (long)(criteria.Page - 1) * criteria.PageSize >= totalCount)
        {
            return new PagedResponse<DocumentSummaryResponse>([], criteria.Page, criteria.PageSize, totalCount);
        }

        var items = await ApplySorting(query, criteria.SortBy, criteria.SortDirection)
            .Skip((criteria.Page - 1) * criteria.PageSize)
            .Take(criteria.PageSize)
            .Select(document => new DocumentSummaryResponse(
                document.Id.Value,
                document.FileName.Value,
                document.Status.ToString(),
                document.SizeInBytes,
                document.ChunkCount,
                document.UploadedBy,
                document.UploadedAtUtc,
                document.ProcessedAtUtc))
            .ToListAsync(cancellationToken);

        return new PagedResponse<DocumentSummaryResponse>(items, criteria.Page, criteria.PageSize, totalCount);
    }

    /// <summary>Construye la consulta filtrada (sin ordenar ni paginar), reutilizada para el recuento.</summary>
    private IQueryable<Document> ApplyFilters(DocumentListCriteria criteria)
    {
        IQueryable<Document> query;

        if (criteria.Search is { Length: > 0 } search)
        {
            // FileName es un Value Object con conversión de valor: EF no traduce FileName.Value
            // dentro de un filtro, así que la búsqueda se resuelve con SQL parametrizado y el
            // resto de la consulta se compone encima con LINQ (EF lo envuelve como subconsulta).
            var pattern = $"%{EscapeLikePattern(search)}%";

            query = dbContext.Documents.FromSql($"""
                SELECT d.*
                FROM documents AS d
                WHERE d.file_name ILIKE {pattern} ESCAPE '\'
                """);
        }
        else
        {
            query = dbContext.Documents;
        }

        query = query.AsNoTracking();

        if (criteria.Statuses.Count > 0)
        {
            var statuses = criteria.Statuses.ToArray();
            query = query.Where(document => statuses.Contains(document.Status));
        }

        // Npgsql solo admite DateTimeOffset con desplazamiento 0 para timestamptz.
        if (criteria.UploadedFrom is { } uploadedFrom)
        {
            var fromUtc = uploadedFrom.ToUniversalTime();
            query = query.Where(document => document.UploadedAtUtc >= fromUtc);
        }

        if (criteria.UploadedTo is { } uploadedTo)
        {
            var toUtc = uploadedTo.ToUniversalTime();
            query = query.Where(document => document.UploadedAtUtc < toUtc);
        }

        return query;
    }

    /// <summary>
    /// Ordena por el campo solicitado y desempata por identificador (UUID v7) para que la
    /// paginación sea estable aunque haya valores repetidos.
    /// </summary>
    private static IOrderedQueryable<Document> ApplySorting(
        IQueryable<Document> query,
        DocumentSortField sortBy,
        SortDirection direction)
    {
        var descending = direction == SortDirection.Desc;

        var ordered = sortBy switch
        {
            DocumentSortField.FileName => query.OrderByDirection(document => document.FileName, descending),
            DocumentSortField.Size => query.OrderByDirection(document => document.SizeInBytes, descending),
            // Los documentos aún no procesados (NULL) quedan siempre al final, en ambos sentidos.
            DocumentSortField.ProcessedAt => query
                .OrderBy(document => document.ProcessedAtUtc == null)
                .ThenByDirection(document => document.ProcessedAtUtc, descending),
            _ => query.OrderByDirection(document => document.UploadedAtUtc, descending)
        };

        return ordered.ThenByDirection(document => document.Id, descending);
    }

    /// <summary>Escapa los comodines de LIKE (<c>%</c>, <c>_</c>) y el propio carácter de escape.</summary>
    private static string EscapeLikePattern(string value)
    {
        var builder = new StringBuilder(value.Length + 8);

        foreach (var character in value)
        {
            if (character is '%' or '_' or LikeEscapeCharacter)
            {
                builder.Append(LikeEscapeCharacter);
            }

            builder.Append(character);
        }

        return builder.ToString();
    }
}

/// <summary>Ordenación con sentido variable sobre <see cref="IQueryable{T}"/>.</summary>
file static class QueryableOrderingExtensions
{
    extension<TSource>(IQueryable<TSource> source)
    {
        public IOrderedQueryable<TSource> OrderByDirection<TKey>(Expression<Func<TSource, TKey>> keySelector, bool descending) =>
            descending ? source.OrderByDescending(keySelector) : source.OrderBy(keySelector);
    }

    extension<TSource>(IOrderedQueryable<TSource> source)
    {
        public IOrderedQueryable<TSource> ThenByDirection<TKey>(Expression<Func<TSource, TKey>> keySelector, bool descending) =>
            descending ? source.ThenByDescending(keySelector) : source.ThenBy(keySelector);
    }
}
