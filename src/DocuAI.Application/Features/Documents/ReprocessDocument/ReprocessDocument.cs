using DocuAI.Application.Abstractions.Messaging;
using DocuAI.Application.Abstractions.Services;
using DocuAI.Domain.Common;
using DocuAI.Domain.Documents;
using FluentValidation;

namespace DocuAI.Application.Features.Documents.ReprocessDocument;

/// <summary>Reencola un documento cuya ingesta falló (acción administrativa).</summary>
public sealed record ReprocessDocumentCommand(Guid DocumentId) : ICommand;

internal sealed class ReprocessDocumentCommandValidator : AbstractValidator<ReprocessDocumentCommand>
{
    public ReprocessDocumentCommandValidator() =>
        RuleFor(command => command.DocumentId).NotEmpty();
}

internal sealed class ReprocessDocumentCommandHandler(
    IDocumentRepository documentRepository,
    IUnitOfWork unitOfWork,
    IDocumentIngestionQueue ingestionQueue)
    : ICommandHandler<ReprocessDocumentCommand>
{
    public async Task<Result> Handle(ReprocessDocumentCommand command, CancellationToken cancellationToken)
    {
        var document = await documentRepository.GetByIdAsync(new DocumentId(command.DocumentId), cancellationToken);
        if (document is null)
        {
            return DocumentErrors.NotFound(command.DocumentId);
        }

        var resetResult = document.ResetForReprocessing();
        if (resetResult.IsFailure)
        {
            return resetResult;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await ingestionQueue.EnqueueAsync(document.Id, cancellationToken);

        return Result.Success();
    }
}
