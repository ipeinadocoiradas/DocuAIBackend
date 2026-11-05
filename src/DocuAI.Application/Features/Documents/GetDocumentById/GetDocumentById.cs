using DocuAI.Application.Abstractions.Messaging;
using DocuAI.Application.Abstractions.Services;
using DocuAI.Domain.Common;
using DocuAI.Domain.Documents;
using FluentValidation;

namespace DocuAI.Application.Features.Documents.GetDocumentById;

/// <summary>Obtiene el detalle y el estado de ingesta de un documento.</summary>
public sealed record GetDocumentByIdQuery(Guid DocumentId) : IQuery<DocumentDetailsResponse>;

internal sealed class GetDocumentByIdQueryValidator : AbstractValidator<GetDocumentByIdQuery>
{
    public GetDocumentByIdQueryValidator() =>
        RuleFor(query => query.DocumentId).NotEmpty();
}

internal sealed class GetDocumentByIdQueryHandler(IDocumentReadService readService)
    : IQueryHandler<GetDocumentByIdQuery, DocumentDetailsResponse>
{
    public async Task<Result<DocumentDetailsResponse>> Handle(
        GetDocumentByIdQuery query,
        CancellationToken cancellationToken)
    {
        var details = await readService.GetDetailsAsync(query.DocumentId, cancellationToken);

        return details is null
            ? DocumentErrors.NotFound(query.DocumentId)
            : details;
    }
}
