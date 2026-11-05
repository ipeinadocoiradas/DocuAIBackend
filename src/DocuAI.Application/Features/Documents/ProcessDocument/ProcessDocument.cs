using DocuAI.Application.Abstractions.Errors;
using DocuAI.Application.Abstractions.Messaging;
using DocuAI.Application.Abstractions.Services;
using DocuAI.Application.Options;
using DocuAI.Domain.Common;
using DocuAI.Domain.Documents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocuAI.Application.Features.Documents.ProcessDocument;

/// <summary>
/// Procesa (fragmenta + genera embeddings + indexa) un documento pendiente.
/// Lo invoca el worker de ingesta en segundo plano.
/// </summary>
public sealed record ProcessDocumentCommand(Guid DocumentId) : ICommand;

internal sealed partial class ProcessDocumentCommandHandler(
    IDocumentRepository documentRepository,
    IUnitOfWork unitOfWork,
    ITextChunker textChunker,
    IEmbeddingService embeddingService,
    IOptions<IngestionOptions> ingestionOptions,
    TimeProvider timeProvider,
    ILogger<ProcessDocumentCommandHandler> logger)
    : ICommandHandler<ProcessDocumentCommand>
{
    private readonly IngestionOptions _options = ingestionOptions.Value;

    public async Task<Result> Handle(ProcessDocumentCommand command, CancellationToken cancellationToken)
    {
        var document = await documentRepository.GetByIdAsync(new DocumentId(command.DocumentId), cancellationToken);
        if (document is null)
        {
            return DocumentErrors.NotFound(command.DocumentId);
        }

        var startResult = document.StartProcessing(timeProvider.GetUtcNow());

        // Se persiste también en caso de fallo: StartProcessing puede marcar el documento
        // como fallido al superar el máximo de intentos.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (startResult.IsFailure)
        {
            return startResult;
        }

        var chunksResult = await BuildIndexedChunksAsync(document, cancellationToken);

        var finalResult = chunksResult.IsSuccess
            ? document.CompleteProcessing(chunksResult.Value, timeProvider.GetUtcNow())
            : chunksResult;

        if (finalResult.IsFailure)
        {
            document.MarkAsFailed($"[{finalResult.Error.Code}] {finalResult.Error.Description}");
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (finalResult.IsSuccess)
        {
            LogIndexed(logger, document.Id.Value, document.FileName.Value, document.ChunkCount);
        }

        return finalResult;
    }

    /// <summary>
    /// Fragmenta el texto y genera los embeddings en lotes controlados con pausas
    /// explícitas entre ellos para respetar las cuotas RPM/RPD del proveedor gratuito.
    /// </summary>
    private async Task<Result<IReadOnlyCollection<DocumentChunk>>> BuildIndexedChunksAsync(
        Document document,
        CancellationToken cancellationToken)
    {
        var textChunks = textChunker.Split(document.ExtractedText, _options.ChunkSizeTokens, _options.ChunkOverlapTokens);
        if (textChunks.Count == 0)
        {
            return DocumentErrors.NoChunks;
        }

        var batches = textChunks.Chunk(_options.EmbeddingBatchSize).ToArray();
        var indexedChunks = new List<DocumentChunk>(textChunks.Count);

        LogChunked(logger, document.Id.Value, textChunks.Count, batches.Length);

        foreach (var (batchNumber, batch) in batches.Index())
        {
            if (batchNumber > 0 && _options.DelayBetweenBatches > TimeSpan.Zero)
            {
                await Task.Delay(_options.DelayBetweenBatches, timeProvider, cancellationToken);
            }

            var embeddingsResult = await embeddingService.GenerateAsync(
                [.. batch.Select(chunk => chunk.Text)],
                cancellationToken);

            if (embeddingsResult.IsFailure)
            {
                return embeddingsResult.Error;
            }

            var embeddings = embeddingsResult.Value;
            if (embeddings.Count != batch.Length)
            {
                return AiErrors.EmbeddingCountMismatch(batch.Length, embeddings.Count);
            }

            for (var i = 0; i < batch.Length; i++)
            {
                var chunkResult = DocumentChunk.Create(
                    document.Id,
                    batch[i].Index,
                    batch[i].Text,
                    batch[i].TokenCount,
                    embeddings[i]);

                if (chunkResult.IsFailure)
                {
                    return chunkResult.Error;
                }

                indexedChunks.Add(chunkResult.Value);
            }

            LogBatchCompleted(logger, document.Id.Value, batchNumber + 1, batches.Length);
        }

        return Result.Success<IReadOnlyCollection<DocumentChunk>>(indexedChunks);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Documento {DocumentId} fragmentado en {ChunkCount} fragmentos ({BatchCount} lotes de embeddings)")]
    private static partial void LogChunked(ILogger logger, Guid documentId, int chunkCount, int batchCount);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Documento {DocumentId}: lote {BatchNumber}/{BatchCount} de embeddings completado")]
    private static partial void LogBatchCompleted(ILogger logger, Guid documentId, int batchNumber, int batchCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Documento {DocumentId} ('{FileName}') indexado con {ChunkCount} fragmentos")]
    private static partial void LogIndexed(ILogger logger, Guid documentId, string fileName, int chunkCount);
}
