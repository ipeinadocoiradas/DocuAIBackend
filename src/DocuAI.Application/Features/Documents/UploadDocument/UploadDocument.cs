using DocuAI.Application.Abstractions.Errors;
using DocuAI.Application.Abstractions.Messaging;
using DocuAI.Application.Abstractions.Services;
using DocuAI.Domain.Common;
using DocuAI.Domain.Documents;
using FluentValidation;

namespace DocuAI.Application.Features.Documents.UploadDocument;

/// <summary>
/// Sube un documento a la base de conocimiento. El texto se extrae de forma síncrona y
/// la fragmentación + embeddings se delegan a la cola de ingesta en segundo plano.
/// </summary>
/// <param name="FileName">Nombre original del fichero.</param>
/// <param name="ContentType">Tipo MIME declarado.</param>
/// <param name="SizeInBytes">Tamaño del fichero.</param>
/// <param name="Content">Flujo con el contenido (propiedad del llamador).</param>
public sealed record UploadDocumentCommand(
    string FileName,
    string ContentType,
    long SizeInBytes,
    Stream Content) : ICommand<UploadDocumentResponse>;

/// <summary>Respuesta de la subida: el documento queda encolado.</summary>
public sealed record UploadDocumentResponse(Guid DocumentId, string FileName, string Status);

internal sealed class UploadDocumentCommandValidator : AbstractValidator<UploadDocumentCommand>
{
    public UploadDocumentCommandValidator()
    {
        RuleFor(command => command.FileName)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("El nombre del fichero es obligatorio.")
            .MaximumLength(DocumentRules.MaxFileNameLength)
            .Must(fileName => DocumentRules.AllowedExtensions.Contains(Path.GetExtension(fileName)))
            .WithMessage($"Formato no soportado. Formatos admitidos: {string.Join(", ", DocumentRules.AllowedExtensions.Order())}.");

        RuleFor(command => command.ContentType)
            .NotEmpty().WithMessage("El tipo de contenido es obligatorio.")
            .MaximumLength(DocumentRules.MaxContentTypeLength);

        RuleFor(command => command.SizeInBytes)
            .GreaterThan(0).WithMessage("El fichero está vacío.")
            .LessThanOrEqualTo(DocumentRules.MaxFileSizeBytes)
            .WithMessage($"El fichero supera el máximo de {DocumentRules.MaxFileSizeBytes / (1024 * 1024)} MB.");

        RuleFor(command => command.Content)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .Must(stream => stream.CanRead).WithMessage("El contenido del fichero no se puede leer.");
    }
}

internal sealed class UploadDocumentCommandHandler(
    IDocumentRepository documentRepository,
    IUnitOfWork unitOfWork,
    ITextExtractor textExtractor,
    IDocumentIngestionQueue ingestionQueue,
    ICurrentUser currentUser,
    TimeProvider timeProvider)
    : ICommandHandler<UploadDocumentCommand, UploadDocumentResponse>
{
    public async Task<Result<UploadDocumentResponse>> Handle(
        UploadDocumentCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { Length: > 0 } uploadedBy)
        {
            return AuthErrors.UserNotIdentified;
        }

        var fileNameResult = FileName.Create(command.FileName);
        if (fileNameResult.IsFailure)
        {
            return fileNameResult.Error;
        }

        var fileName = fileNameResult.Value;

        var textResult = await textExtractor.ExtractAsync(command.Content, fileName, cancellationToken);
        if (textResult.IsFailure)
        {
            return textResult.Error;
        }

        // Deduplicación por contenido: evita indexar dos veces el mismo texto (y gastar cuota).
        var contentHash = ContentHash.FromText(textResult.Value);
        if (await documentRepository.ExistsWithHashAsync(contentHash, cancellationToken))
        {
            return DocumentErrors.Duplicate(fileName.Value);
        }

        var documentResult = Document.Create(
            fileName,
            command.ContentType,
            command.SizeInBytes,
            textResult.Value,
            uploadedBy,
            timeProvider.GetUtcNow());

        if (documentResult.IsFailure)
        {
            return documentResult.Error;
        }

        var document = documentResult.Value;

        documentRepository.Add(document);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await ingestionQueue.EnqueueAsync(document.Id, cancellationToken);

        return new UploadDocumentResponse(document.Id.Value, document.FileName.Value, document.Status.ToString());
    }
}
