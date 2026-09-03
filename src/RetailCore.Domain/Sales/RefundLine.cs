using RetailCore.Domain.Common;
using RetailCore.Domain.Monetary;
using RetailCore.Domain.Tax;

namespace RetailCore.Domain.Sales;

/// <summary>
/// One line being given back, tied to the line of the original sale it reverses.
/// </summary>
public sealed class RefundLine : Entity<RefundLineId>, IBelongsToShop
{
    private RefundLine(
        RefundLineId id,
        RefundId refundId,
        ShopId shopId,
        SaleLineId originalSaleLineId,
        ProductId productId,
        string sku,
        string description,
        Quantity quantity,
        Money amount,
        TaxTreatment taxTreatment)
        : base(id)
    {
        RefundId = refundId;
        ShopId = shopId;
        OriginalSaleLineId = originalSaleLineId;
        ProductId = productId;
        Sku = sku;
        Description = description;
        Quantity = quantity;
        Amount = amount;
        TaxTreatment = taxTreatment;
    }

    private RefundLine()
    {
        Sku = null!;
        Description = null!;
        TaxTreatment = null!;
    }

    public RefundId RefundId { get; private set; }

    /// <summary>TEN-01. Carried on the line so row level security can cover this table too.</summary>
    public ShopId ShopId { get; private set; }

    /// <summary>The line on the original sale. RET-02 allows refunding a subset of them.</summary>
    public SaleLineId OriginalSaleLineId { get; private set; }

    public ProductId ProductId { get; private set; }

    public string Sku { get; private set; }

    public string Description { get; private set; }

    /// <summary>How much is coming back. May be less than the quantity sold. RET-02.</summary>
    public Quantity Quantity { get; private set; }

    /// <summary>
    /// The money being returned for this line, tax inclusive.
    /// </summary>
    /// <remarks>
    /// Derived from what was actually charged after discounts, not from the shelf price. A
    /// customer who bought at 20% off and returns the item gets the discounted amount back,
    /// otherwise the discount becomes a way to extract cash from the drawer.
    /// </remarks>
    public Money Amount { get; private set; }

    /// <summary>Copied from the original line so the tax reverses at the rate charged. CMP-02.</summary>
    public TaxTreatment TaxTreatment { get; private set; }

    /// <summary>The tax being given back, contained within <see cref="Amount"/>.</summary>
    public Money TaxAmount => TaxTreatment.TaxWithin(Amount);

    internal static RefundLine Create(
        RefundId refundId,
        ShopId shopId,
        SaleLine originalLine,
        Quantity quantity,
        Money amount,
        RefundLineId? id = null)
    {
        ArgumentNullException.ThrowIfNull(originalLine);

        return new RefundLine(
            id ?? RefundLineId.New(),
            refundId,
            shopId,
            originalLine.Id,
            originalLine.ProductId,
            originalLine.Sku,
            originalLine.Description,
            quantity,
            amount,
            originalLine.TaxTreatment);
    }
}
