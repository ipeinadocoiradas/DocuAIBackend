namespace DocuAI.Domain.Common;

/// <summary>
/// Confirma de forma atómica los cambios realizados sobre los agregados.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>Persiste los cambios pendientes.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
