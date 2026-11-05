using DocuAI.Domain.Documents;
using Microsoft.EntityFrameworkCore;
using Pgvector;

namespace DocuAI.Infrastructure.Persistence.Repositories;

/// <summary>Implementación EF Core + pgvector de <see cref="IDocumentRepository"/>.</summary>
internal sealed class DocumentRepository(ApplicationDbContext dbContext) : IDocumentRepository
{
    public Task<Document?> GetByIdAsync(DocumentId id, CancellationToken cancellationToken = default) =>
        dbContext.Documents.FirstOrDefaultAsync(document => document.Id == id, cancellationToken);

    public Task<bool> ExistsWithHashAsync(ContentHash contentHash, CancellationToken cancellationToken = default) =>
        dbContext.Documents.AnyAsync(document => document.ContentHash == contentHash, cancellationToken);

    public Task<bool> AnyProcessedAsync(CancellationToken cancellationToken = default) =>
        dbContext.Documents.AnyAsync(document => document.Status == DocumentStatus.Processed, cancellationToken);

    public async Task<IReadOnlyList<DocumentId>> GetIdsPendingProcessingAsync(CancellationToken cancellationToken = default) =>
        await dbContext.Documents
            .AsNoTracking()
            .Where(document => document.Status == DocumentStatus.Pending || document.Status == DocumentStatus.Processing)
            .OrderBy(document => document.UploadedAtUtc)
            .Select(document => document.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ChunkSearchResult>> SearchSimilarChunksAsync(
        float[] queryEmbedding,
        int topK,
        CancellationToken cancellationToken = default)
    {
        var queryVector = new Vector(queryEmbedding);
        var processedStatus = nameof(DocumentStatus.Processed);

        // Búsqueda ANN por distancia coseno (<=>) aprovechando el índice HNSW.
        // Similitud coseno = 1 - distancia coseno. Los valores interpolados se parametrizan.
        var rows = await dbContext.Database
            .SqlQuery<ChunkSearchRow>($"""
                SELECT c.document_id                   AS "DocumentId",
                       d.file_name                     AS "FileName",
                       c.chunk_index                   AS "ChunkIndex",
                       c.content                       AS "Content",
                       1 - (c.embedding <=> {queryVector}) AS "Similarity"
                FROM document_chunks AS c
                INNER JOIN documents AS d ON d.id = c.document_id
                WHERE d.status = {processedStatus}
                ORDER BY c.embedding <=> {queryVector}
                LIMIT {topK}
                """)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(row => new ChunkSearchResult(
            new DocumentId(row.DocumentId),
            row.FileName,
            row.ChunkIndex,
            row.Content,
            row.Similarity))];
    }

    public void Add(Document document) => dbContext.Documents.Add(document);

    /// <summary>
    /// Marca el documento para eliminación. Los fragmentos no necesitan cargarse: la FK
    /// <c>document_chunks.document_id</c> está definida con <c>ON DELETE CASCADE</c>.
    /// </summary>
    public void Remove(Document document) => dbContext.Documents.Remove(document);

    /// <summary>Fila sin mapear devuelta por la consulta vectorial.</summary>
    internal sealed class ChunkSearchRow
    {
        public Guid DocumentId { get; init; }

        public string FileName { get; init; } = string.Empty;

        public int ChunkIndex { get; init; }

        public string Content { get; init; } = string.Empty;

        public double Similarity { get; init; }
    }
}
