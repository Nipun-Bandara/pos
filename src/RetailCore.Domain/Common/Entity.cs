namespace RetailCore.Domain.Common;

/// <summary>
/// An object with an identity that persists across changes to its values.
/// </summary>
/// <typeparam name="TId">The strongly typed identifier.</typeparam>
public abstract class Entity<TId>
    where TId : struct, IEquatable<TId>
{
    protected Entity(TId id) => Id = id;

    /// <summary>Required by EF Core materialisation.</summary>
    protected Entity()
    {
    }

    public TId Id { get; protected set; }

    public override bool Equals(object? obj) =>
        obj is Entity<TId> other && other.GetType() == GetType() && other.Id.Equals(Id);

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);
}
