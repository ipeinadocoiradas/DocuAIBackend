using DocuAI.Domain.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pgvector;

namespace DocuAI.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapeo de <see cref="DocumentChunk"/> a <c>document_chunks</c>, con la columna
/// <c>vector(n)</c> de pgvector y un índice HNSW por distancia coseno.
/// </summary>
/// <param name="embeddingDimensions">Dimensión del vector de embedding.</param>
internal sealed class DocumentChunkConfiguration(int embeddingDimensions) : IEntityTypeConfiguration<DocumentChunk>
{
    public const string TableName = "document_chunks";

    public void Configure(EntityTypeBuilder<DocumentChunk> builder)
    {
        builder.ToTable(TableName);

        builder.HasKey(chunk => chunk.Id);

        builder.Property(chunk => chunk.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new DocumentChunkId(value))
            .ValueGeneratedNever();

        builder.Property(chunk => chunk.DocumentId)
            .HasColumnName("document_id")
            .HasConversion(id => id.Value, value => new DocumentId(value))
            .IsRequired();

        builder.Property(chunk => chunk.Index).HasColumnName("chunk_index");

        builder.Property(chunk => chunk.Content)
            .HasColumnName("content")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(chunk => chunk.TokenCount).HasColumnName("token_count");

        // El dominio trabaja con float[] (sin dependencias); aquí se convierte al tipo Vector de pgvector.
        builder.Property(chunk => chunk.Embedding)
            .HasColumnName("embedding")
            .HasColumnType($"vector({embeddingDimensions})")
            .HasConversion(
                embedding => new Vector(embedding),
                vector => vector.ToArray(),
                // Árboles de expresión: se evita Span/Index y se invoca Enumerable explícitamente
                // (en C# 14 las conversiones implícitas a Span cambian la resolución de sobrecargas).
                new ValueComparer<float[]>(
                    (left, right) => Enumerable.SequenceEqual(left!, right!),
                    embedding => embedding.Length == 0 ? 0 : HashCode.Combine(embedding.Length, embedding[0], embedding[embedding.Length - 1]),
                    embedding => Enumerable.ToArray(embedding)))
            .IsRequired();

        builder.HasIndex(chunk => new { chunk.DocumentId, chunk.Index }).IsUnique();

        // Índice ANN HNSW con operador de distancia coseno (<=>).
        builder.HasIndex(chunk => chunk.Embedding)
            .HasMethod("hnsw")
            .HasOperators("vector_cosine_ops")
            .HasDatabaseName("ix_document_chunks_embedding_hnsw");
    }
}
