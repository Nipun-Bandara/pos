using RetailCore.Domain.Catalogue;
using RetailCore.Domain.Common;
using RetailCore.Domain.Monetary;
using RetailCore.Domain.Sales;
using RetailCore.Domain.Tax;

namespace RetailCore.Domain.Tests;

/// <summary>
/// A minimart set up once so the specifications can say what they mean.
/// </summary>
/// <remarks>
/// Trades in rupees, where the smallest coin is one rupee, because that makes the cash
/// rounding rule (PAY-02) visible in every payment test rather than something only one
/// test remembers to check.
/// </remarks>
public sealed class TestShop
{
    public static readonly Currency Currency = Currency.LKR;

    public static readonly DateTimeOffset Now = new(2026, 3, 14, 9, 30, 0, TimeSpan.FromHours(5.5));

    public ShopId ShopId { get; } = ShopId.New();

    public TerminalId TerminalId { get; } = TerminalId.New();

    public ShiftId ShiftId { get; } = ShiftId.New();

    public UserId CashierId { get; } = UserId.New();

    public UserId ManagerId { get; } = UserId.New();

    public CategoryId GroceryId { get; } = CategoryId.New();

    public TaxRateId StandardRateId { get; } = TaxRateId.New();

    /// <summary>18% VAT, the standard rate in these specifications.</summary>
    public TaxTreatment StandardVat { get; } = TaxTreatment.Standard("VAT18", 0.18m);

    /// <summary>CMP-03: reportable, but nothing is charged.</summary>
    public TaxTreatment ZeroRated { get; } = TaxTreatment.ZeroRated();

    /// <summary>CMP-03: outside the tax entirely.</summary>
    public TaxTreatment Exempt { get; } = TaxTreatment.Exempt();

    public Money Rupees(decimal amount) => Money.FromMajor(amount, Currency);

    /// <summary>A product sold by the item.</summary>
    public Product Item(decimal price, decimal cost = 0m, string name = "Tinned fish", string sku = "TIN-001") =>
        Product.Create(
            ShopId,
            sku,
            name,
            GroceryId,
            UnitOfMeasure.Each,
            Rupees(price),
            Rupees(decimal.Round(cost == 0m ? price * 0.7m : cost, 2)),
            StandardRateId);

    /// <summary>A product sold by weight. SAL-05.</summary>
    public Product Weighed(decimal pricePerKilo, string name = "Tomatoes", string sku = "VEG-TOM") =>
        Product.Create(
            ShopId,
            sku,
            name,
            GroceryId,
            UnitOfMeasure.Kilogram,
            Rupees(pricePerKilo),
            Rupees(decimal.Round(pricePerKilo * 0.6m, 2)),
            StandardRateId);

    /// <summary>SAL-13.</summary>
    public Product OpenPriced(string name = "Hardware sundry", string sku = "HW-MISC")
    {
        var product = Product.Create(
            ShopId,
            sku,
            name,
            GroceryId,
            UnitOfMeasure.Each,
            Money.Zero(Currency),
            Money.Zero(Currency),
            StandardRateId);

        product.SetOpenPrice(true);
        return product;
    }

    public Sale StartSale() =>
        Sale.Start(ShopId, TerminalId, ShiftId, CashierId, Currency, Now);

    public Sale StartSale(DiscountPolicy policy) =>
        Sale.Start(ShopId, TerminalId, ShiftId, CashierId, Currency, Now, policy);

    public static ReceiptNumber NextReceipt(long sequence = 1) => ReceiptNumber.Create("T1", sequence);
}
