namespace DocuAI.Domain.Common;

/// <summary>
/// Clase base para entidades con identidad. Dos entidades son iguales si
/// comparten tipo e identificador.
/// </summary>
/// <typeparam name="TId">Tipo del identificador fuertemente tipado.</typeparam>
public abstract class Entity<TId> : IEquatable<Entity<TId>>
    where TId : struct, IEquatable<TId>
{
    protected Entity(TId id) => Id = id;

    /// <summary>Constructor reservado para la materialización desde EF Core.</summary>
    protected Entity()
    {
    }

    /// <summary>Identificador de la entidad.</summary>
    public TId Id { get; private init; }

    public bool Equals(Entity<TId>? other) =>
        other is not null && other.GetType() == GetType() && other.Id.Equals(Id);

    public override bool Equals(object? obj) => obj is Entity<TId> other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);

    public static bool operator ==(Entity<TId>? left, Entity<TId>? right) => Equals(left, right);

    public static bool operator !=(Entity<TId>? left, Entity<TId>? right) => !Equals(left, right);
}
