using RetailCore.Domain.Common;
using RetailCore.Domain.Monetary;

namespace RetailCore.Domain.Inventory;

/// <summary>Why stock moved. Every movement has a reason, none are unexplained.</summary>
public enum StockMovementKind
{
    /// <summary>Goods sold. INV-03.</summary>
    Sale = 0,

    /// <summary>Goods returned by a customer. RET-06.</summary>
    Refund = 1,

    /// <summary>A completed sale was reversed. RET-04.</summary>
    SaleVoided = 2,

    /// <summary>Goods received note. INV-04.</summary>
    GoodsReceived = 3,

    /// <summary>Manual correction with a reason code. INV-05.</summary>
    Adjustment = 4,

    /// <summary>Damaged, expired or otherwise written off. INV-06.</summary>
    Wastage = 5,

    /// <summary>Variance found by counting. INV-07.</summary>
    StockTake = 6,

    /// <summary>Moved to another branch. TEN-07.</summary>
    Transfer = 7,

    /// <summary>The count a product started with.</summary>
    OpeningBalance = 8,
}

public enum StockDirection
{
    /// <summary>Into the shop. Increases stock on hand.</summary>
    In = 1,

    /// <summary>Out of the shop. Decreases stock on hand.</summary>
    Out = -1,
}

/// <summary>
/// One entry in the append-only stock ledger. INV-01.
/// </summary>
/// <remarks>
/// <para>
/// INV-02: there is no "quantity on hand" column anywhere in this system. Stock on hand is
/// the sum of this ledger for a product. A mutable counter would be wrong the first time two
/// offline tills sold the last unit at the same time, and there would be no way to find out
/// how it got wrong. A ledger reconciles by construction and explains itself.
/// </para>
/// <para>
/// Signed balances can represent an owner-approved shortage. The application write
/// boundary checks availability and requires exact, reasoned owner approval before a
/// posting can make stock negative.
/// </para>
/// <para>
/// Rows are never updated or deleted. A mistake is corrected by a compensating movement, so
/// the history of what was believed and when stays intact. CMP-04.
/// </para>
/// </remarks>
public sealed class StockMovement : Entity<StockMovementId>, IBelongsToShop
{
    private StockMovement(
        StockMovementId id,
        ShopId shopId,
        ProductId productId,
        Quantity quantity,
        StockDirection direction,
        StockMovementKind kind,
        DateTimeOffset occurredAt,
        string? reason,
        UserId? recordedBy,
        string? sourceType,
        Guid? sourceId,
        Money? unitCost)
        : base(id)
    {
        ShopId = shopId;
        ProductId = productId;
        Quantity = quantity;
        Direction = direction;
        Kind = kind;
        OccurredAt = occurredAt;
        Reason = reason;
        RecordedBy = recordedBy;
        SourceType = sourceType;
        SourceId = sourceId;
        UnitCost = unitCost;
    }

    private StockMovement()
    {
    }

    public string? PostingKey { get; private set; }
    public ProductId? EnteredProductId { get; private set; }
    public long InputQuantityThousandths { get; private set; }
    public long ConversionFactorThousandths { get; private set; } = 1000;
    public string? BaseUnit { get; private set; }
    public long? InputUnitCostMinor { get; private set; }
    public string? InputCostCurrency { get; private set; }
    public UserId? ApprovedBy { get; private set; }
    public string? Reference { get; private set; }

    public static StockMovement Posted(ShopId shop, ProductId enteredProduct, ProductId baseProduct,
        Quantity inputQuantity, long factorThousandths, string baseUnit, StockDirection direction,
        StockMovementKind kind, string sourceType, Guid sourceId, DateTimeOffset occurredAt,
        UserId actor, string reason, Money inputUnitCost, UserId? approvedBy = null, string? reference = null)
    {
        if (!Enum.IsDefined(direction) || !Enum.IsDefined(kind) || inputUnitCost.IsNegative
            || string.IsNullOrWhiteSpace(reason) || reason.Length > 500 || reference?.Length > 200
            || string.IsNullOrWhiteSpace(baseUnit) || sourceId == Guid.Empty)
            throw new DomainException("InvalidStockEntry");
        var quantity = StockConversion.ToBase(inputQuantity, factorThousandths);
        var baseCost = Money.FromMajorRounded(inputUnitCost.ToDecimal() * 1000m / factorThousandths, inputUnitCost.Currency);
        return new StockMovement(StockMovementId.New(), shop, baseProduct, quantity, direction, kind,
            occurredAt, reason.Trim(), actor, sourceType, sourceId, baseCost)
        {
            PostingKey = $"{sourceType}:{sourceId:N}:{kind}:{enteredProduct.Value:N}:{baseProduct.Value:N}:{factorThousandths}",
            EnteredProductId = enteredProduct, InputQuantityThousandths = inputQuantity.Thousandths,
            ConversionFactorThousandths = factorThousandths, BaseUnit = baseUnit,
            InputUnitCostMinor = inputUnitCost.MinorUnits, InputCostCurrency = inputUnitCost.CurrencyCode,
            ApprovedBy = approvedBy, Reference = reference,
        };
    }

    public ShopId ShopId { get; private set; }

    public ProductId ProductId { get; private set; }

    /// <summary>Always positive. The sign lives in <see cref="Direction"/>.</summary>
    public Quantity Quantity { get; private set; }

    public StockDirection Direction { get; private set; }

    public StockMovementKind Kind { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>Required for adjustments and wastage. INV-05, INV-06.</summary>
    public string? Reason { get; private set; }

    public UserId? RecordedBy { get; private set; }

    /// <summary>What caused this, for example "Sale" or "Refund". Traces back to the document.</summary>
    public string? SourceType { get; private set; }

    public Guid? SourceId { get; private set; }

    /// <summary>Cost at the time of the movement, for valuing stock. BO-08.</summary>
    public Money? UnitCost { get; private set; }

    /// <summary>The movement as a signed number, for summing the ledger.</summary>
    public decimal SignedQuantity => Direction == StockDirection.In
        ? Quantity.Value
        : -Quantity.Value;

    /// <summary>Goods leaving because they were sold. INV-03.</summary>
    public static StockMovement ForSale(
        ShopId shopId,
        ProductId productId,
        Quantity quantity,
        SaleId saleId,
        DateTimeOffset occurredAt,
        UserId? recordedBy = null,
        Money? unitCost = null) =>
        new(
            StockMovementId.New(),
            shopId,
            productId,
            quantity,
            StockDirection.Out,
            StockMovementKind.Sale,
            occurredAt,
            null,
            recordedBy,
            nameof(Sales.Sale),
            saleId.Value,
            unitCost);

    /// <summary>Goods coming back from a customer. RET-06.</summary>
    public static StockMovement ForRefund(
        ShopId shopId,
        ProductId productId,
        Quantity quantity,
        RefundId refundId,
        DateTimeOffset occurredAt,
        UserId? recordedBy = null,
        Money? unitCost = null) =>
        new(
            StockMovementId.New(),
            shopId,
            productId,
            quantity,
            StockDirection.In,
            StockMovementKind.Refund,
            occurredAt,
            null,
            recordedBy,
            nameof(Sales.Refund),
            refundId.Value,
            unitCost);

    /// <summary>Goods put back because a completed sale was reversed. RET-04.</summary>
    public static StockMovement ForVoidedSale(
        ShopId shopId,
        ProductId productId,
        Quantity quantity,
        SaleId saleId,
        DateTimeOffset occurredAt,
        UserId? recordedBy = null) =>
        new(
            StockMovementId.New(),
            shopId,
            productId,
            quantity,
            StockDirection.In,
            StockMovementKind.SaleVoided,
            occurredAt,
            null,
            recordedBy,
            nameof(Sales.Sale),
            saleId.Value,
            null);

    /// <summary>A movement entered by hand, which always needs a reason. INV-05, INV-06.</summary>
    /// <exception cref="DomainException">No reason was given.</exception>
    public static StockMovement Manual(
        ShopId shopId,
        ProductId productId,
        Quantity quantity,
        StockDirection direction,
        StockMovementKind kind,
        string reason,
        UserId recordedBy,
        DateTimeOffset occurredAt,
        Money? unitCost = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (!quantity.IsPositive)
        {
            throw new DomainException("A stock movement needs a quantity greater than zero.");
        }

        if (kind is StockMovementKind.Sale or StockMovementKind.Refund or StockMovementKind.SaleVoided)
        {
            throw new DomainException(
                $"{kind} movements are posted by the system from a sale, not entered by hand.");
        }

        return new StockMovement(
            StockMovementId.New(),
            shopId,
            productId,
            quantity,
            direction,
            kind,
            occurredAt,
            reason.Trim(),
            recordedBy,
            null,
            null,
            unitCost);
    }
}
