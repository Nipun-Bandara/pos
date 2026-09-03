namespace RetailCore.Domain.Common;

/// <summary>
/// An aggregate that has raised events, seen without knowing its identifier type.
/// </summary>
/// <remarks>
/// The unit of work needs to sweep the change tracker for anything with pending events, and
/// it cannot do that through an open generic base class.
/// </remarks>
public interface IHasDomainEvents
{
    IReadOnlyList<IDomainEvent> DomainEvents { get; }

    void ClearDomainEvents();
}

/// <summary>
/// The single entry point for changes to a cluster of entities that must stay consistent.
/// </summary>
/// <typeparam name="TId">The strongly typed identifier.</typeparam>
public abstract class AggregateRoot<TId> : Entity<TId>, IHasDomainEvents
    where TId : struct, IEquatable<TId>
{
    private readonly List<IDomainEvent> _domainEvents = [];

    protected AggregateRoot(TId id)
        : base(id)
    {
    }

    /// <summary>Required by EF Core materialisation.</summary>
    protected AggregateRoot()
    {
    }

    /// <summary>
    /// Events raised since the aggregate was loaded, awaiting dispatch at commit time.
    /// </summary>
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents;

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    /// <summary>Called by the unit of work once the events have been dispatched.</summary>
    public void ClearDomainEvents() => _domainEvents.Clear();
}
