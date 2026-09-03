using RetailCore.Domain.Common;
using RetailCore.Domain.Monetary;

namespace RetailCore.Domain.Catalogue;

/// <summary>
/// Something the shop sells. CAT-01, CAT-03, CAT-04.
/// </summary>
/// <summary>
/// One of a product's barcodes. CAT-07.
/// </summary>
/// <remarks>
/// Its own type rather than a bare string so that persistence can give it a table and an
/// index. SAL-01 has a sub-100ms budget from scan to line, which a scan of a JSON column
/// would not meet once a catalogue reaches a few thousand products.
/// </remarks>
public sealed class ProductBarcode
{
    internal ProductBarcode(string code) => Code = code;

    private ProductBarcode() => Code = null!;

    public string Code { get; private set; }

    public override string ToString() => Code;
}

/// <inheritdoc cref="Product"/>
public sealed class Product : Entity<ProductId>, IBelongsToShop
{
    private readonly List<ProductBarcode> _barcodes = [];

    private Product(
        ProductId id,
        ShopId shopId,
        string sku,
        string name,
        CategoryId categoryId,
        UnitOfMeasure unit,
        Money unitPrice,
        Money unitCost,
        TaxRateId taxRateId)
        : base(id)
    {
        ShopId = shopId;
        Sku = sku;
        Name = name;
        CategoryId = categoryId;
        Unit = unit;
        UnitPrice = unitPrice;
        UnitCost = unitCost;
        TaxRateId = taxRateId;
    }

    private Product()
    {
        Sku = null!;
        Name = null!;
    }

    public ShopId ShopId { get; private set; }

    /// <summary>The shop's own code for the product. Unique within the shop.</summary>
    public string Sku { get; private set; }

    public string Name { get; private set; }

    public CategoryId CategoryId { get; private set; }

    public UnitOfMeasure Unit { get; private set; }

    /// <summary>Shelf price, tax inclusive. CMP-01.</summary>
    public Money UnitPrice { get; private set; }

    /// <summary>
    /// What the shop paid. CAT-03, kept so the margin report (BO-08) works from a snapshot
    /// taken at the time of sale rather than from today's cost.
    /// </summary>
    public Money UnitCost { get; private set; }

    public TaxRateId TaxRateId { get; private set; }

    /// <summary>
    /// CAT-07: a product can carry several barcodes, because manufacturers change them and
    /// the old stock stays on the shelf for months.
    /// </summary>
    public IReadOnlyList<ProductBarcode> Barcodes => _barcodes;

    /// <summary>SAL-13: the cashier enters the price at the till.</summary>
    public bool IsOpenPrice { get; private set; }

    /// <summary>True when the till should prompt for a weight. SAL-05.</summary>
    public bool IsWeighed => Unit.AllowsFractionalQuantity();

    /// <summary>INV-08: alert when stock on hand falls to this level.</summary>
    public Quantity ReorderLevel { get; private set; } = Quantity.Zero;

    public bool IsActive { get; private set; } = true;

    public static Product Create(
        ShopId shopId,
        string sku,
        string name,
        CategoryId categoryId,
        UnitOfMeasure unit,
        Money unitPrice,
        Money unitCost,
        TaxRateId taxRateId,
        ProductId? id = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sku);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (unitPrice.IsNegative)
        {
            throw new DomainException($"Product '{name}' cannot have a negative price.");
        }

        if (unitCost.IsNegative)
        {
            throw new DomainException($"Product '{name}' cannot have a negative cost.");
        }

        if (unitPrice.Currency != unitCost.Currency)
        {
            throw new DomainException(
                $"Product '{name}' has a price in {unitPrice.Currency.Code} but a cost in {unitCost.Currency.Code}.");
        }

        return new Product(
            id ?? ProductId.New(),
            shopId,
            sku.Trim(),
            name.Trim(),
            categoryId,
            unit,
            unitPrice,
            unitCost,
            taxRateId);
    }

    public void AddBarcode(string barcode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(barcode);

        var normalised = barcode.Trim();
        if (!HasBarcode(normalised))
        {
            _barcodes.Add(new ProductBarcode(normalised));
        }
    }

    public bool HasBarcode(string barcode) =>
        _barcodes.Exists(b => string.Equals(b.Code, barcode, StringComparison.Ordinal));

    public void RemoveBarcode(string barcode) => _barcodes.RemoveAll(b => b.Code == barcode);

    public void Reprice(Money unitPrice)
    {
        if (unitPrice.IsNegative)
        {
            throw new DomainException($"Product '{Name}' cannot have a negative price.");
        }

        UnitPrice = unitPrice;
    }

    public void RecordCost(Money unitCost)
    {
        if (unitCost.IsNegative)
        {
            throw new DomainException($"Product '{Name}' cannot have a negative cost.");
        }

        UnitCost = unitCost;
    }

    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
    }

    public void MoveToCategory(CategoryId categoryId) => CategoryId = categoryId;

    public void ApplyTaxRate(TaxRateId taxRateId) => TaxRateId = taxRateId;

    public void SetOpenPrice(bool isOpenPrice) => IsOpenPrice = isOpenPrice;

    public void SetReorderLevel(Quantity level) => ReorderLevel = level;

    public void Deactivate() => IsActive = false;

    public void Reactivate() => IsActive = true;
}
