using DocuAI.Domain.Common;

namespace DocuAI.Domain.Documents;

/// <summary>
/// Fragmento semántico de un documento junto con su vector de embedding.
/// Pertenece al agregado <see cref="Document"/>.
/// </summary>
public sealed class DocumentChunk : Entity<DocumentChunkId>
{
    private DocumentChunk(
        DocumentChunkId id,
        DocumentId documentId,
        int index,
        string content,
        int tokenCount,
        float[] embedding)
        : base(id)
    {
        DocumentId = documentId;
        Index = index;
        Content = content;
        TokenCount = tokenCount;
        Embedding = embedding;
    }

    /// <summary>Constructor reservado para EF Core.</summary>
    private DocumentChunk()
    {
    }

    public DocumentId DocumentId { get; private set; }

    /// <summary>Posición del fragmento dentro del documento (base 0).</summary>
    public int Index { get; private set; }

    public string Content { get; private set; } = string.Empty;

    public int TokenCount { get; private set; }

    /// <summary>Representación vectorial del contenido.</summary>
    public float[] Embedding { get; private set; } = [];

    /// <summary>Crea un fragmento validando sus invariantes.</summary>
    public static Result<DocumentChunk> Create(
        DocumentId documentId,
        int index,
        string content,
        int tokenCount,
        float[] embedding)
    {
        if (index < 0)
        {
            return DocumentErrors.ChunkInvalidIndex;
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return DocumentErrors.ChunkContentEmpty;
        }

        if (embedding is null or { Length: 0 })
        {
            return DocumentErrors.ChunkEmbeddingEmpty;
        }

        return new DocumentChunk(DocumentChunkId.New(), documentId, index, content, tokenCount, embedding);
    }
}
