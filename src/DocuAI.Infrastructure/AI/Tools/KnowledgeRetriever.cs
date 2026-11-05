using DocuAI.Application.Abstractions.Services;
using DocuAI.Domain.Common;
using DocuAI.Domain.Documents;

namespace DocuAI.Infrastructure.AI.Tools;

/// <summary>Recupera los fragmentos más relevantes para una consulta en lenguaje natural.</summary>
internal interface IKnowledgeRetriever
{
    Task<Result<IReadOnlyList<ChunkSearchResult>>> RetrieveAsync(
        string query,
        int topK,
        double minSimilarity,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Búsqueda semántica: embedding de la consulta + búsqueda vectorial coseno Top K
/// filtrada por un umbral mínimo de similitud.
/// </summary>
internal sealed class KnowledgeRetriever(
    IEmbeddingService embeddingService,
    IDocumentRepository documentRepository) : IKnowledgeRetriever
{
    public async Task<Result<IReadOnlyList<ChunkSearchResult>>> RetrieveAsync(
        string query,
        int topK,
        double minSimilarity,
        CancellationToken cancellationToken = default)
    {
        var embeddingResult = await embeddingService.GenerateAsync([query], cancellationToken);
        if (embeddingResult.IsFailure)
        {
            return embeddingResult.Error;
        }

        var matches = await documentRepository.SearchSimilarChunksAsync(
            embeddingResult.Value[0],
            topK,
            cancellationToken);

        return Result.Success<IReadOnlyList<ChunkSearchResult>>(
            [.. matches.Where(match => match.Similarity >= minSimilarity)]);
    }
}
