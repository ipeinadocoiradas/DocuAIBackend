using DocuAI.Domain.Common;
using DocuAI.Domain.Documents;
using DocuAI.Infrastructure.Options;
using DocuAI.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DocuAI.Infrastructure.Persistence;

/// <summary>
/// Contexto EF Core de la aplicación. Implementa <see cref="IUnitOfWork"/>.
/// </summary>
public sealed class ApplicationDbContext(
    DbContextOptions<ApplicationDbContext> options,
    IOptions<EmbeddingOptions> embeddingOptions)
    : DbContext(options), IUnitOfWork
{
    public DbSet<Document> Documents => Set<Document>();

    public DbSet<DocumentChunk> DocumentChunks => Set<DocumentChunk>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Habilita la extensión pgvector en la migración inicial.
        modelBuilder.HasPostgresExtension("vector");

        modelBuilder.ApplyConfiguration(new DocumentConfiguration());
        modelBuilder.ApplyConfiguration(new DocumentChunkConfiguration(embeddingOptions.Value.Dimensions));
    }
}
