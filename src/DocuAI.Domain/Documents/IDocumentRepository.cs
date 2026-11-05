namespace DocuAI.Domain.Documents;

/// <summary>
/// Fragmento recuperado por búsqueda semántica, con su grado de similitud (coseno).
/// </summary>
/// <param name="DocumentId">Documento de origen.</param>
/// <param name="FileName">Nombre del fichero de origen (para citar la fuente).</param>
/// <param name="ChunkIndex">Posición del fragmento en el documento.</param>
/// <param name="Content">Texto del fragmento.</param>
/// <param name="Similarity">Similitud coseno en el rango [-1, 1]; mayor es más relevante.</param>
public sealed record ChunkSearchResult(
    DocumentId DocumentId,
    string FileName,
    int ChunkIndex,
    string Content,
    double Similarity);

/// <summary>
/// Puerto de persistencia del agregado <see cref="Document"/>.
/// </summary>
public interface IDocumentRepository
{
    Task<Document?> GetByIdAsync(DocumentId id, CancellationToken cancellationToken = default);

    Task<bool> ExistsWithHashAsync(ContentHash contentHash, CancellationToken cancellationToken = default);

    /// <summary>Indica si existe al menos un documento indexado y consultable.</summary>
    Task<bool> AnyProcessedAsync(CancellationToken cancellationToken = default);

    /// <summary>Documentos pendientes o interrumpidos que deben volver a la cola.</summary>
    Task<IReadOnlyList<DocumentId>> GetIdsPendingProcessingAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Recupera los <paramref name="topK"/> fragmentos más cercanos (distancia coseno)
    /// al vector de consulta, considerando solo documentos procesados.
    /// </summary>
    Task<IReadOnlyList<ChunkSearchResult>> SearchSimilarChunksAsync(
        float[] queryEmbedding,
        int topK,
        CancellationToken cancellationToken = default);

    void Add(Document document);

    /// <summary>Marca el documento para eliminación; se hace efectivo al guardar la unidad de trabajo.</summary>
    void Remove(Document document);
}
