using RetailCore.Domain.Common;
using RetailCore.Domain.Monetary;

namespace RetailCore.Domain.Sales;

/// <summary>A product and how much of it moved, used to drive the stock ledger.</summary>
public sealed record SoldItem(ProductId ProductId, Quantity Quantity, ProductId? StockProductId = null,
    long FactorThousandths = 1000, RetailCore.Domain.Catalogue.UnitOfMeasure BaseUnit = RetailCore.Domain.Catalogue.UnitOfMeasure.Each,
    Money? UnitCost = null, bool Saleable = true);

/// <summary>
/// A sale was paid for and finished.
/// </summary>
/// <remarks>
/// INV-03: the stock ledger is written by a handler for this event, in the same transaction
/// as the sale. Nothing decrements stock directly, so there is exactly one path from
/// "goods left the shop" to "the ledger says so".
/// </remarks>
public sealed record SaleCompleted(
    SaleId SaleId,
    ShopId ShopId,
    TerminalId TerminalId,
    ShiftId ShiftId,
    UserId CashierId,
    ReceiptNumber ReceiptNumber,
    Money Total,
    IReadOnlyList<SoldItem> Items,
    DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>
/// A completed sale was reversed. RET-04.
/// </summary>
/// <remarks>
/// Carries the items so the handler can put the stock back (RET-06) without reloading the
/// sale, and carries the reason and authoriser for the audit log (SEC-06).
/// </remarks>
public sealed record SaleVoided(
    SaleId SaleId,
    ShopId ShopId,
    TerminalId TerminalId,
    ReceiptNumber ReceiptNumber,
    Money Total,
    string Reason,
    UserId AuthorisedBy,
    IReadOnlyList<SoldItem> Items,
    DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>A line was removed before completion. SAL-06, recorded for SEC-06.</summary>
public sealed record SaleLineVoided(
    SaleId SaleId,
    ShopId ShopId,
    SaleLineId SaleLineId,
    string Description,
    Money Amount,
    string Reason,
    UserId? VoidedBy,
    DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>
/// A discount was given. SAL-07 and SAL-08, recorded for SEC-06 whether or not it needed
/// approval, because the pattern of small unapproved discounts is itself the signal.
/// </summary>
public sealed record DiscountApplied(
    SaleId SaleId,
    ShopId ShopId,
    SaleLineId? SaleLineId,
    Money Amount,
    string Reason,
    UserId? AuthorisedBy,
    DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>A refund was issued against an earlier sale. RET-01.</summary>
public sealed record RefundIssued(
    RefundId RefundId,
    SaleId OriginalSaleId,
    ShopId ShopId,
    TerminalId TerminalId,
    ShiftId ShiftId,
    UserId CashierId,
    Money Total,
    string Reason,
    UserId AuthorisedBy,
    IReadOnlyList<SoldItem> Items,
    DateTimeOffset OccurredAt) : IDomainEvent;
