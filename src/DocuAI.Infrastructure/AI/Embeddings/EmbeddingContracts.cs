using System.Text.Json.Serialization;

namespace DocuAI.Infrastructure.AI.Embeddings;

/// <summary>Petición <c>/embeddings</c> en formato OpenAI.</summary>
internal sealed record EmbeddingRequest(
    string Model,
    IReadOnlyList<string> Input,
    int? Dimensions)
{
    public string EncodingFormat { get; init; } = "float";
}

/// <summary>Respuesta <c>/embeddings</c> en formato OpenAI.</summary>
internal sealed record EmbeddingResponse(
    IReadOnlyList<EmbeddingItem>? Data,
    string? Model,
    ProviderError? Error);

internal sealed record EmbeddingItem(int Index, float[] Embedding);

internal sealed record ProviderError(int? Code, string? Message);

/// <summary>Respuesta <c>GET /models</c> en formato OpenAI (Ollama la expone en <c>/v1/models</c>).</summary>
internal sealed record ModelListResponse(IReadOnlyList<ModelItem>? Data);

internal sealed record ModelItem(string Id);

/// <summary>Serialización JSON generada en compilación (sin reflexión).</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(EmbeddingRequest))]
[JsonSerializable(typeof(EmbeddingResponse))]
[JsonSerializable(typeof(ModelListResponse))]
internal sealed partial class EmbeddingJsonContext : JsonSerializerContext
{
}
