using DocuAI.Application.Abstractions.Messaging;
using DocuAI.Application.Abstractions.Services;
using DocuAI.Domain.Common;
using DocuAI.Domain.Documents;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace DocuAI.Application.Features.Documents.DeleteDocument;

/// <summary>
/// Elimina un documento de la base de conocimiento junto con todos sus fragmentos y
/// embeddings (acción administrativa). Deja de estar disponible para el agente RAG de inmediato.
/// </summary>
public sealed record DeleteDocumentCommand(Guid DocumentId) : ICommand;

internal sealed class DeleteDocumentCommandValidator : AbstractValidator<DeleteDocumentCommand>
{
    public DeleteDocumentCommandValidator() =>
        RuleFor(command => command.DocumentId).NotEmpty();
}

internal sealed partial class DeleteDocumentCommandHandler(
    IDocumentRepository documentRepository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ILogger<DeleteDocumentCommandHandler> logger)
    : ICommandHandler<DeleteDocumentCommand>
{
    public async Task<Result> Handle(DeleteDocumentCommand command, CancellationToken cancellationToken)
    {
        var document = await documentRepository.GetByIdAsync(new DocumentId(command.DocumentId), cancellationToken);
        if (document is null)
        {
            return DocumentErrors.NotFound(command.DocumentId);
        }

        // Regla de dominio: no se elimina mientras el worker lo está indexando.
        var canBeDeleted = document.EnsureCanBeDeleted();
        if (canBeDeleted.IsFailure)
        {
            return canBeDeleted;
        }

        // Los fragmentos no se cargan: la FK document_chunks → documents tiene ON DELETE CASCADE,
        // por lo que PostgreSQL elimina fragmentos y vectores en la misma sentencia y transacción.
        documentRepository.Remove(document);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        LogDeleted(logger, document.Id.Value, document.FileName.Value, document.ChunkCount, currentUser.UserName ?? currentUser.UserId ?? "desconocido");

        return Result.Success();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Documento {DocumentId} ('{FileName}') eliminado con {ChunkCount} fragmentos por {DeletedBy}")]
    private static partial void LogDeleted(ILogger logger, Guid documentId, string fileName, int chunkCount, string deletedBy);
}
