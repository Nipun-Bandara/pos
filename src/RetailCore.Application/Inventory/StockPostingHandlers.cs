using RetailCore.Application.Abstractions;
using RetailCore.Domain.Inventory;
using RetailCore.Domain.Sales;

namespace RetailCore.Application.Inventory;

/// <summary>
/// Turns a completed sale into stock movements. INV-03.
/// </summary>
/// <remarks>
/// This runs inside the sale's own transaction, so a sale and the ledger entries that
/// explain it are written together or not at all. Nothing else in the system decrements
/// stock, which is what makes the ledger a complete account of where goods went.
/// </remarks>
public sealed class PostStockForCompletedSale(IStockLedger ledger) : IDomainEventHandler<SaleCompleted>
{
    public Task HandleAsync(SaleCompleted domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        ledger.AddRange(domainEvent.Items.Select(item => StockMovement.ForSale(
            domainEvent.ShopId,
            item.ProductId,
            item.Quantity,
            domainEvent.SaleId,
            domainEvent.OccurredAt,
            domainEvent.CashierId)));

        return Task.CompletedTask;
    }
}

/// <summary>Puts stock back when a completed sale is reversed. RET-04.</summary>
public sealed class PostStockForVoidedSale(IStockLedger ledger) : IDomainEventHandler<SaleVoided>
{
    public Task HandleAsync(SaleVoided domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        ledger.AddRange(domainEvent.Items.Select(item => StockMovement.ForVoidedSale(
            domainEvent.ShopId,
            item.ProductId,
            item.Quantity,
            domainEvent.SaleId,
            domainEvent.OccurredAt,
            domainEvent.AuthorisedBy)));

        return Task.CompletedTask;
    }
}

/// <summary>Puts stock back when goods are returned. RET-06.</summary>
public sealed class PostStockForRefund(IStockLedger ledger) : IDomainEventHandler<RefundIssued>
{
    public Task HandleAsync(RefundIssued domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        ledger.AddRange(domainEvent.Items.Select(item => StockMovement.ForRefund(
            domainEvent.ShopId,
            item.ProductId,
            item.Quantity,
            domainEvent.RefundId,
            domainEvent.OccurredAt,
            domainEvent.CashierId)));

        return Task.CompletedTask;
    }
}
