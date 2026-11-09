using RetailCore.Application.Abstractions;
using RetailCore.Domain.Common;
using RetailCore.Domain.Inventory;
using RetailCore.Domain.Monetary;
using RetailCore.Domain.Sales;

namespace RetailCore.Application.Inventory;

/// <summary>Builds stock effects from historical sale snapshots, never today's catalogue mapping.</summary>
public static class StockPosting
{
    public static IReadOnlyList<StockMovement> Movements(ShopId shop, string sourceType, Guid sourceId,
        IReadOnlyList<SoldItem> items, StockDirection direction, StockMovementKind kind,
        DateTimeOffset occurredAt, UserId actor, Currency currency, string reason,
        UserId? approvedBy = null) => items.Where(i => kind != StockMovementKind.Refund || i.Saleable)
        .GroupBy(i => new { i.ProductId, StockProduct = i.StockProductId ?? i.ProductId,
            i.FactorThousandths, i.BaseUnit, i.UnitCost })
        .Select(g => StockMovement.Posted(shop, g.Key.ProductId, g.Key.StockProduct,
            Quantity.FromThousandths(g.Sum(i => i.Quantity.Thousandths)), g.Key.FactorThousandths,
            g.Key.BaseUnit.ToString(), direction, kind, sourceType, sourceId, occurredAt, actor,
            reason, g.Key.UnitCost ?? Money.Zero(currency), approvedBy)).ToArray();
}

public sealed class PostStockForCompletedSale(IStockLedger ledger) : IDomainEventHandler<SaleCompleted>
{
    public Task HandleAsync(SaleCompleted domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        return ledger.PostAsync(domainEvent.ShopId, "Sale", domainEvent.SaleId.Value, StockPosting.Movements(domainEvent.ShopId, "Sale", domainEvent.SaleId.Value,
            domainEvent.Items, StockDirection.Out, StockMovementKind.Sale, domainEvent.OccurredAt,
            domainEvent.CashierId, domainEvent.Total.Currency, "Sale"), cancellationToken);
    }
}

public sealed class PostStockForVoidedSale(IStockLedger ledger) : IDomainEventHandler<SaleVoided>
{
    public Task HandleAsync(SaleVoided domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        return ledger.PostAsync(domainEvent.ShopId, "SaleVoid", domainEvent.SaleId.Value, StockPosting.Movements(domainEvent.ShopId, "SaleVoid", domainEvent.SaleId.Value,
            domainEvent.Items, StockDirection.In, StockMovementKind.SaleVoided, domainEvent.OccurredAt,
            domainEvent.AuthorisedBy, domainEvent.Total.Currency, domainEvent.Reason), cancellationToken);
    }
}

public sealed class PostStockForRefund(IStockLedger ledger) : IDomainEventHandler<RefundIssued>
{
    public Task HandleAsync(RefundIssued domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        return ledger.PostAsync(domainEvent.ShopId, "Refund", domainEvent.RefundId.Value, StockPosting.Movements(domainEvent.ShopId, "Refund", domainEvent.RefundId.Value,
            domainEvent.Items, StockDirection.In, StockMovementKind.Refund, domainEvent.OccurredAt,
            domainEvent.CashierId, domainEvent.Total.Currency, domainEvent.Reason), cancellationToken);
    }
}
