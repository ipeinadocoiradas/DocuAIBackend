using System.ComponentModel.DataAnnotations;

namespace DocuAI.Application.Options;

/// <summary>
/// Parámetros de la ingesta: fragmentación y throttling de la generación de embeddings
/// para respetar las cuotas RPM/RPD del proveedor.
/// </summary>
public sealed class IngestionOptions : IValidatableObject
{
    public const string SectionName = "Ingestion";

    /// <summary>Tamaño máximo de cada fragmento en tokens.</summary>
    [Range(50, 8000)]
    public int ChunkSizeTokens { get; init; } = 500;

    /// <summary>Tokens compartidos entre fragmentos consecutivos.</summary>
    [Range(0, 4000)]
    public int ChunkOverlapTokens { get; init; } = 100;

    /// <summary>Número de fragmentos enviados por petición de embeddings.</summary>
    [Range(1, 256)]
    public int EmbeddingBatchSize { get; init; } = 32;

    /// <summary>
    /// Pausa explícita entre lotes: protege la cuota de un proveedor remoto o evita monopolizar
    /// la CPU/GPU de un proveedor local mientras se atienden consultas de chat.
    /// </summary>
    public TimeSpan DelayBetweenBatches { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Capacidad de la cola en memoria de documentos pendientes.</summary>
    [Range(1, 10_000)]
    public int QueueCapacity { get; init; } = 100;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ChunkOverlapTokens >= ChunkSizeTokens)
        {
            yield return new ValidationResult(
                "El solapamiento debe ser menor que el tamaño del fragmento.",
                [nameof(ChunkOverlapTokens), nameof(ChunkSizeTokens)]);
        }

        if (DelayBetweenBatches < TimeSpan.Zero || DelayBetweenBatches > TimeSpan.FromMinutes(5))
        {
            yield return new ValidationResult(
                "La pausa entre lotes debe estar entre 0 y 5 minutos.",
                [nameof(DelayBetweenBatches)]);
        }
    }
}

/// <summary>Parámetros funcionales del chat RAG.</summary>
public sealed class ChatOptions : IValidatableObject
{
    public const string SectionName = "Chat";

    /// <summary>Fragmentos recuperados por defecto (Top K).</summary>
    [Range(1, 50)]
    public int DefaultTopK { get; init; } = 5;

    /// <summary>Máximo Top K que puede solicitar un cliente.</summary>
    [Range(1, 50)]
    public int MaxTopK { get; init; } = 20;

    /// <summary>Longitud máxima de la pregunta.</summary>
    [Range(10, 10_000)]
    public int MaxQuestionLength { get; init; } = 2000;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (DefaultTopK > MaxTopK)
        {
            yield return new ValidationResult(
                "DefaultTopK no puede ser mayor que MaxTopK.",
                [nameof(DefaultTopK), nameof(MaxTopK)]);
        }
    }
}
