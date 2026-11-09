using RetailCore.Domain.Catalogue;
using RetailCore.Domain.Common;
using RetailCore.Domain.Monetary;
using RetailCore.Domain.Tax;

namespace RetailCore.Domain.Sales;

/// <summary>
/// One product on one sale, with the catalogue values as they stood at the time. CAT-05.
/// </summary>
/// <remarks>
/// Description, price, cost and tax treatment are copied from the product rather than
/// referenced. A receipt reprinted next year has to show what was charged then, and the
/// margin report (BO-08) has to use the cost that applied on the day, not today's cost.
/// The ProductId is kept as well, so reporting can still group by product.
/// </remarks>
public sealed class SaleLine : Entity<SaleLineId>, IBelongsToShop
{
    private SaleLine(
        SaleLineId id,
        SaleId saleId,
        ShopId shopId,
        int lineNumber,
        ProductId productId,
        string sku,
        string description,
        UnitOfMeasure unit,
        Money unitPrice,
        Money unitCost,
        Quantity quantity,
        TaxTreatment taxTreatment)
        : base(id)
    {
        SaleId = saleId;
        ShopId = shopId;
        LineNumber = lineNumber;
        ProductId = productId;
        Sku = sku;
        Description = description;
        Unit = unit;
        UnitPrice = unitPrice;
        UnitCost = unitCost;
        Quantity = quantity;
        TaxTreatment = taxTreatment;
        ApportionedSaleDiscount = Money.Zero(unitPrice.Currency);
    }

    private SaleLine()
    {
        Sku = null!;
        Description = null!;
        TaxTreatment = null!;
    }

    public SaleId SaleId { get; private set; }

    /// <summary>TEN-01. Carried on the line so row level security can cover this table too.</summary>
    public ShopId ShopId { get; private set; }

    /// <summary>Position on the receipt, starting at 1. Stable once assigned.</summary>
    public int LineNumber { get; private set; }

    public ProductId ProductId { get; private set; }

    public string Sku { get; private set; }

    /// <summary>The product name as printed. Snapshotted.</summary>
    public string Description { get; private set; }

    public ProductId? StockProductId { get; private set; }
    public long StockFactorThousandths { get; private set; } = 1000;
    public UnitOfMeasure? StockBaseUnit { get; private set; }
    internal void SnapshotStock(Product product)
    {
        StockProductId = product.StockBaseProductId ?? product.Id;
        StockFactorThousandths = product.StockFactorThousandths;
        StockBaseUnit = product.StockBaseUnit ?? product.Unit;
    }
    public string? SinhalaNameSnapshot { get; private set; }
    public string? EnglishNameSnapshot { get; private set; }
    public string? ReceiptNameSnapshot { get; private set; }
    public int? AppliedPriceBand { get; private set; }
    public long? AppliedMinimumThousandths { get; private set; }
    internal void SnapshotQuote(PriceQuote quote)
    {
        SinhalaNameSnapshot = quote.SinhalaName;
        EnglishNameSnapshot = quote.EnglishName;
        ReceiptNameSnapshot = quote.ReceiptName;
        AppliedPriceBand = quote.Band;
        AppliedMinimumThousandths = quote.MinimumThousandths;
    }
    public UnitOfMeasure Unit { get; private set; }

    /// <summary>Tax-inclusive shelf price at the time of sale.</summary>
    public Money UnitPrice { get; private set; }

    /// <summary>Cost at the time of sale, for margin reporting. CAT-03.</summary>
    public Money UnitCost { get; private set; }

    public Quantity Quantity { get; private set; }

    public TaxTreatment TaxTreatment { get; private set; }

    /// <summary>SAL-07. Null when no line discount was given.</summary>
    public Discount? LineDiscount { get; private set; }

    /// <summary>
    /// This line's share of a whole-sale discount (SAL-08), assigned by the parent sale.
    /// </summary>
    public Money ApportionedSaleDiscount { get; private set; }

    /// <summary>SAL-06. The row survives so the audit trail shows what was rung and removed.</summary>
    public bool IsVoided { get; private set; }

    public string? VoidReason { get; private set; }

    public UserId? VoidedBy { get; private set; }

    public Currency Currency => UnitPrice.Currency;

    /// <summary>Price times quantity, before any discount. Zero once the line is voided.</summary>
    public Money GrossAmount => IsVoided
        ? Money.Zero(Currency)
        : UnitPrice.Extend(Quantity);

    /// <summary>What the line discount takes off. SAL-07.</summary>
    public Money LineDiscountAmount => IsVoided || LineDiscount is null
        ? Money.Zero(Currency)
        : LineDiscount.ComputeOn(UnitPrice.Extend(Quantity));

    /// <summary>Everything taken off this line, including its share of the sale discount.</summary>
    public Money TotalDiscountAmount => LineDiscountAmount + (IsVoided ? Money.Zero(Currency) : ApportionedSaleDiscount);

    /// <summary>What the customer actually pays for this line, tax inclusive.</summary>
    public Money LineTotal => GrossAmount - TotalDiscountAmount;

    /// <summary>The tax contained in <see cref="LineTotal"/>. CMP-01.</summary>
    public Money TaxAmount => TaxTreatment.TaxWithin(LineTotal);

    /// <summary>The tax-exclusive value of the line.</summary>
    public Money NetAmount => LineTotal - TaxAmount;

    /// <summary>Cost of goods for this line, for the margin report. BO-08.</summary>
    public Money CostAmount => IsVoided
        ? Money.Zero(UnitCost.Currency)
        : UnitCost.Extend(Quantity);

    internal static SaleLine Create(
        SaleId saleId,
        ShopId shopId,
        int lineNumber,
        ProductId productId,
        string sku,
        string description,
        UnitOfMeasure unit,
        Money unitPrice,
        Money unitCost,
        Quantity quantity,
        TaxTreatment taxTreatment,
        SaleLineId? id = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sku);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(taxTreatment);

        if (unitPrice.IsNegative)
        {
            throw new DomainException($"'{description}' cannot be sold at a negative price.");
        }

        if (!quantity.IsPositive)
        {
            throw new DomainException($"'{description}' needs a quantity greater than zero.");
        }

        if (!unit.AllowsFractionalQuantity() && !quantity.IsWholeUnits)
        {
            throw new DomainException(
                $"'{description}' is sold by the {unit.Abbreviation()}, so {quantity} is not a valid quantity.");
        }

        if (unitPrice.Currency != unitCost.Currency)
        {
            throw new DomainException(
                $"'{description}' has a price in {unitPrice.Currency.Code} but a cost in {unitCost.Currency.Code}.");
        }

        return new SaleLine(
            id ?? SaleLineId.New(),
            saleId,
            shopId,
            lineNumber,
            productId,
            sku.Trim(),
            description.Trim(),
            unit,
            unitPrice,
            unitCost,
            quantity,
            taxTreatment);
    }

    internal void ChangeQuantity(Quantity quantity)
    {
        if (!quantity.IsPositive)
        {
            throw new DomainException(
                $"'{Description}' needs a quantity greater than zero. Void the line instead.");
        }

        if (!Unit.AllowsFractionalQuantity() && !quantity.IsWholeUnits)
        {
            throw new DomainException(
                $"'{Description}' is sold by the {Unit.Abbreviation()}, so {quantity} is not a valid quantity.");
        }

        Quantity = quantity;
    }

    internal void OverridePrice(Money unitPrice)
    {
        if (unitPrice.IsNegative)
        {
            throw new DomainException($"'{Description}' cannot be sold at a negative price.");
        }

        if (unitPrice.Currency != Currency)
        {
            throw new DomainException(
                $"'{Description}' is priced in {Currency.Code}, not {unitPrice.Currency.Code}.");
        }

        UnitPrice = unitPrice;
    }

    internal void ApplyDiscount(Discount discount) => LineDiscount = discount;

    internal void RemoveDiscount() => LineDiscount = null;

    internal void ApportionSaleDiscount(Money amount) => ApportionedSaleDiscount = amount;

    internal void Void(string reason, UserId? voidedBy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        IsVoided = true;
        VoidReason = reason.Trim();
        VoidedBy = voidedBy;
        ApportionedSaleDiscount = Money.Zero(Currency);
    }
}
