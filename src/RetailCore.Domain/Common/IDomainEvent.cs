namespace RetailCore.Domain.Common;

/// <summary>
/// Something that happened in the domain and that other parts of the system react to.
/// </summary>
/// <remarks>
/// PLAN.md Phase 1: domain events are dispatched on commit, inside the same transaction
/// as the state change. That is what makes INV-03 (sales and refunds post stock movements
/// automatically) reliable rather than best effort.
/// </remarks>
public interface IDomainEvent
{
    /// <summary>When the event happened, in UTC.</summary>
    DateTimeOffset OccurredAt { get; }
}
