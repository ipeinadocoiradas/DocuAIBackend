using System.Threading.Channels;
using DocuAI.Application.Abstractions.Services;
using DocuAI.Application.Options;
using DocuAI.Domain.Documents;
using Microsoft.Extensions.Options;

namespace DocuAI.Infrastructure.Ingestion;

/// <summary>
/// Cola acotada en memoria basada en <see cref="Channel{T}"/>. Ante reinicios, la
/// durabilidad la garantiza la base de datos: el worker reencola los documentos
/// <c>Pending</c>/<c>Processing</c> al arrancar.
/// </summary>
internal sealed class DocumentIngestionQueue : IDocumentIngestionQueue
{
    private readonly Channel<DocumentId> _channel;

    public DocumentIngestionQueue(IOptions<IngestionOptions> options) =>
        _channel = Channel.CreateBounded<DocumentId>(new BoundedChannelOptions(options.Value.QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

    public ValueTask EnqueueAsync(DocumentId documentId, CancellationToken cancellationToken = default) =>
        _channel.Writer.WriteAsync(documentId, cancellationToken);

    /// <summary>Consume la cola hasta la cancelación.</summary>
    public IAsyncEnumerable<DocumentId> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
