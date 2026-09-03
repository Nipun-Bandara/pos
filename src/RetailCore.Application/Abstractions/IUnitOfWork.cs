namespace RetailCore.Application.Abstractions;

/// <summary>
/// One transaction: everything inside it commits or none of it does.
/// </summary>
/// <remarks>
/// PLAN.md Phase 1 requires domain events to be dispatched on commit. That happens inside
/// this boundary, so the stock movements a sale produces (INV-03) and the outbox row that
/// will sync it (SYN-04) land in the same transaction as the sale. If any part fails, the
/// sale does not exist and neither do its consequences. There is no window in which the
/// shop has sold something the ledger does not know about.
/// </remarks>
public interface IUnitOfWork
{
    /// <summary>
    /// Saves everything tracked, dispatching domain events raised along the way.
    /// </summary>
    /// <returns>The number of rows written.</returns>
    Task<int> CommitAsync(CancellationToken cancellationToken = default);
}
