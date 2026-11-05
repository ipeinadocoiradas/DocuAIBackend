using DocuAI.Domain.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DocuAI.Infrastructure.Persistence.Configurations;

/// <summary>Mapeo del agregado <see cref="Document"/> a la tabla <c>documents</c>.</summary>
internal sealed class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.ToTable("documents");

        builder.HasKey(document => document.Id);

        builder.Property(document => document.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new DocumentId(value))
            .ValueGeneratedNever();

        builder.Property(document => document.FileName)
            .HasColumnName("file_name")
            .HasMaxLength(DocumentRules.MaxFileNameLength)
            .HasConversion(fileName => fileName.Value, value => FileName.FromPersistence(value))
            .IsRequired();

        builder.Property(document => document.ContentType)
            .HasColumnName("content_type")
            .HasMaxLength(DocumentRules.MaxContentTypeLength)
            .IsRequired();

        builder.Property(document => document.SizeInBytes)
            .HasColumnName("size_in_bytes");

        builder.Property(document => document.ExtractedText)
            .HasColumnName("extracted_text")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(document => document.ContentHash)
            .HasColumnName("content_hash")
            .HasMaxLength(ContentHash.Length)
            .IsFixedLength()
            .HasConversion(hash => hash.Value, value => new ContentHash(value))
            .IsRequired();

        builder.Property(document => document.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(document => document.UploadedBy)
            .HasColumnName("uploaded_by")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(document => document.UploadedAtUtc).HasColumnName("uploaded_at_utc");
        builder.Property(document => document.ProcessingStartedAtUtc).HasColumnName("processing_started_at_utc");
        builder.Property(document => document.ProcessedAtUtc).HasColumnName("processed_at_utc");
        builder.Property(document => document.ProcessingAttempts).HasColumnName("processing_attempts");
        builder.Property(document => document.ChunkCount).HasColumnName("chunk_count");

        builder.Property(document => document.FailureReason)
            .HasColumnName("failure_reason")
            .HasMaxLength(DocumentRules.MaxFailureReasonLength);

        builder.HasMany(document => document.Chunks)
            .WithOne()
            .HasForeignKey(chunk => chunk.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        // La colección se expone como solo lectura; EF trabaja sobre el campo privado _chunks.
        builder.Navigation(document => document.Chunks)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(document => document.ContentHash).IsUnique();
        builder.HasIndex(document => document.Status);
        builder.HasIndex(document => document.UploadedAtUtc);
    }
}
