using DocuAI.Application.Abstractions.Messaging;
using DocuAI.Application.Abstractions.Services;
using DocuAI.Domain.Common;
using DocuAI.Domain.Documents;
using FluentValidation;

namespace DocuAI.Application.Features.Documents.ListDocuments;

/// <summary>
/// Lista paginada y filtrable de documentos de la base de conocimiento.
/// </summary>
/// <param name="Page">Página (base 1).</param>
/// <param name="PageSize">Elementos por página (máx. <see cref="MaxPageSize"/>).</param>
/// <param name="Search">Texto a buscar en el nombre del fichero (opcional).</param>
/// <param name="Statuses">Estados a incluir (opcional; vacío = todos).</param>
/// <param name="UploadedFrom">Fecha de subida mínima, inclusiva (opcional).</param>
/// <param name="UploadedTo">Fecha de subida máxima, exclusiva (opcional).</param>
/// <param name="SortBy">Campo de ordenación.</param>
/// <param name="SortDirection">Sentido de la ordenación.</param>
public sealed record ListDocumentsQuery(
    int Page = 1,
    int PageSize = ListDocumentsQuery.DefaultPageSize,
    string? Search = null,
    IReadOnlyCollection<DocumentStatus>? Statuses = null,
    DateTimeOffset? UploadedFrom = null,
    DateTimeOffset? UploadedTo = null,
    DocumentSortField SortBy = DocumentSortField.UploadedAt,
    SortDirection SortDirection = SortDirection.Desc) : IQuery<PagedResponse<DocumentSummaryResponse>>
{
    public const int DefaultPageSize = 20;

    public const int MaxPageSize = 100;
}

internal sealed class ListDocumentsQueryValidator : AbstractValidator<ListDocumentsQuery>
{
    public ListDocumentsQueryValidator()
    {
        RuleFor(query => query.Page)
            .GreaterThanOrEqualTo(1)
            .WithMessage("La página debe ser mayor o igual que 1.");

        RuleFor(query => query.PageSize)
            .InclusiveBetween(1, ListDocumentsQuery.MaxPageSize)
            .WithMessage($"El tamaño de página debe estar entre 1 y {ListDocumentsQuery.MaxPageSize}.");

        RuleFor(query => query.Search)
            .MaximumLength(DocumentRules.MaxFileNameLength)
            .WithMessage($"El texto de búsqueda no puede superar {DocumentRules.MaxFileNameLength} caracteres.");

        RuleForEach(query => query.Statuses)
            .IsInEnum()
            .WithMessage($"Estado no válido. Valores admitidos: {string.Join(", ", Enum.GetNames<DocumentStatus>())}.");

        RuleFor(query => query.SortBy)
            .IsInEnum()
            .WithMessage($"Campo de ordenación no válido. Valores admitidos: {string.Join(", ", Enum.GetNames<DocumentSortField>())}.");

        RuleFor(query => query.SortDirection)
            .IsInEnum()
            .WithMessage($"Sentido de ordenación no válido. Valores admitidos: {string.Join(", ", Enum.GetNames<SortDirection>())}.");

        RuleFor(query => query.UploadedTo)
            .GreaterThan(query => query.UploadedFrom)
            .When(query => query.UploadedFrom.HasValue && query.UploadedTo.HasValue)
            .WithMessage("'uploadedTo' debe ser posterior a 'uploadedFrom'.");
    }
}

internal sealed class ListDocumentsQueryHandler(IDocumentReadService readService)
    : IQueryHandler<ListDocumentsQuery, PagedResponse<DocumentSummaryResponse>>
{
    public async Task<Result<PagedResponse<DocumentSummaryResponse>>> Handle(
        ListDocumentsQuery query,
        CancellationToken cancellationToken)
    {
        var criteria = new DocumentListCriteria
        {
            Page = query.Page,
            PageSize = query.PageSize,
            Search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim(),
            Statuses = query.Statuses is { Count: > 0 } statuses ? [.. statuses.Distinct()] : [],
            UploadedFrom = query.UploadedFrom,
            UploadedTo = query.UploadedTo,
            SortBy = query.SortBy,
            SortDirection = query.SortDirection
        };

        return await readService.GetPageAsync(criteria, cancellationToken);
    }
}
