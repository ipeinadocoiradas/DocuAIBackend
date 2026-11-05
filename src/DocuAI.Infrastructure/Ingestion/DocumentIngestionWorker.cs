using DocuAI.Application.Features.Documents.ProcessDocument;
using DocuAI.Domain.Common;
using DocuAI.Domain.Documents;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DocuAI.Infrastructure.Ingestion;

/// <summary>
/// Servicio en segundo plano que consume la cola de ingesta de forma secuencial
/// (un documento cada vez) para controlar el consumo de cuota del proveedor.
/// </summary>
internal sealed partial class DocumentIngestionWorker(
    DocumentIngestionQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<DocumentIngestionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Cede el hilo para no bloquear el arranque del host.
        await Task.Yield();

        await RequeueUnfinishedDocumentsAsync(stoppingToken);

        LogStarted(logger);

        try
        {
            await foreach (var documentId in queue.ReadAllAsync(stoppingToken))
            {
                await ProcessAsync(documentId, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Parada ordenada del host.
        }

        LogStopped(logger);
    }

    private async Task ProcessAsync(DocumentId documentId, CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();

            var result = await sender.Send(new ProcessDocumentCommand(documentId.Value), stoppingToken);

            if (result.IsFailure)
            {
                // Un documento encolado puede haberse eliminado antes de llegar su turno: no es un fallo.
                if (result.Error.Type == ErrorType.NotFound)
                {
                    LogSkippedDeleted(logger, documentId.Value);
                }
                else
                {
                    LogProcessingFailed(logger, documentId.Value, result.Error.Code, result.Error.Description);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // El documento queda en 'Processing' y se reanudará en el siguiente arranque.
            throw;
        }
        catch (Exception exception)
        {
            // Fallo inesperado de infraestructura (BD caída, etc.): se registra y el worker continúa.
            LogUnexpectedError(logger, exception, documentId.Value);
        }
    }

    private async Task RequeueUnfinishedDocumentsAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

            var pendingIds = await repository.GetIdsPendingProcessingAsync(stoppingToken);

            foreach (var documentId in pendingIds)
            {
                await queue.EnqueueAsync(documentId, stoppingToken);
            }

            if (pendingIds.Count > 0)
            {
                LogRequeued(logger, pendingIds.Count);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogRequeueFailed(logger, exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Worker de ingesta iniciado")]
    private static partial void LogStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Worker de ingesta detenido")]
    private static partial void LogStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Reencolados {Count} documentos pendientes de ejecuciones anteriores")]
    private static partial void LogRequeued(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "No se pudieron reencolar los documentos pendientes")]
    private static partial void LogRequeueFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "La ingesta del documento {DocumentId} ha fallado: {ErrorCode} - {ErrorDescription}")]
    private static partial void LogProcessingFailed(ILogger logger, Guid documentId, string errorCode, string errorDescription);

    [LoggerMessage(Level = LogLevel.Information, Message = "Documento {DocumentId} descartado de la cola: se eliminó antes de procesarse")]
    private static partial void LogSkippedDeleted(ILogger logger, Guid documentId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error inesperado procesando el documento {DocumentId}")]
    private static partial void LogUnexpectedError(ILogger logger, Exception exception, Guid documentId);
}
