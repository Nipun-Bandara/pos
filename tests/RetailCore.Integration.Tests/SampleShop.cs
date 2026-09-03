using RetailCore.Domain.Catalogue;
using RetailCore.Domain.Common;
using RetailCore.Domain.Monetary;
using RetailCore.Domain.Sales;
using RetailCore.Domain.Security;
using RetailCore.Domain.Shifts;
using RetailCore.Domain.Tax;
using RetailCore.Domain.Tenancy;

namespace RetailCore.Integration.Tests;

/// <summary>
/// A shop with enough in it to ring up a real sale, built the same way on both providers.
/// </summary>
/// <remarks>
/// Deliberately the same objects for SQLite and PostgreSQL. PLAN.md Phase 1 exits when a sale
/// round-trips through both and reads back with identical totals, and that comparison is only
/// meaningful if both sides started from identical input.
/// </remarks>
internal sealed class SampleShop
{
    public static readonly Currency Currency = Currency.LKR;

    public static readonly DateTimeOffset Now =
        new(2026, 3, 14, 9, 30, 0, TimeSpan.Zero);

    private SampleShop(
        Shop shop,
        Terminal terminal,
        User cashier,
        User manager,
        Category category,
        TaxRate standardRate,
        Product tinnedFish,
        Product tomatoes,
        Product rice,
        Shift shift)
    {
        Shop = shop;
        Terminal = terminal;
        Cashier = cashier;
        Manager = manager;
        Category = category;
        StandardRate = standardRate;
        TinnedFish = tinnedFish;
        Tomatoes = tomatoes;
        Rice = rice;
        Shift = shift;
    }

    public Shop Shop { get; }

    public Terminal Terminal { get; }

    public User Cashier { get; }

    public User Manager { get; }

    public Category Category { get; }

    public TaxRate StandardRate { get; }

    /// <summary>Taxable, sold by the item.</summary>
    public Product TinnedFish { get; }

    /// <summary>Taxable, sold by weight. SAL-05.</summary>
    public Product Tomatoes { get; }

    /// <summary>Exempt, so the basket exercises CMP-03.</summary>
    public Product Rice { get; }

    public Shift Shift { get; }

    public TaxTreatment StandardVat => StandardRate.ToTreatment();

    public TaxTreatment Exempt { get; } = TaxTreatment.Exempt();

    public static Money Rupees(decimal amount) => Money.FromMajor(amount, Currency);

    public static SampleShop Create(string name = "Sunrise Minimart", string terminalCode = "T1")
    {
        var shop = Shop.Create(name, $"{name} (Pvt) Ltd", Currency, "Asia/Colombo");
        shop.SetTaxRegistration("VAT-114477");

        var terminal = Terminal.Create(shop.Id, terminalCode, "Front counter");
        terminal.Enrol(Now);

        var cashier = User.Create(shop.Id, "Nimal", Role.Cashier, "argon2id$stub$cashier");
        var manager = User.Create(shop.Id, "Kanthi", Role.Manager, "argon2id$stub$manager");

        var category = Category.Create(shop.Id, "Grocery");

        var standardRate = TaxRate.Create(
            shop.Id,
            "VAT18",
            "VAT at 18%",
            TaxCategory.Standard,
            0.18m,
            new DateOnly(2026, 1, 1));

        var tinnedFish = Product.Create(
            shop.Id,
            "TIN-001",
            "Tinned fish 425g",
            category.Id,
            UnitOfMeasure.Each,
            Rupees(118.00m),
            Rupees(82.60m),
            standardRate.Id);
        tinnedFish.AddBarcode("4791234567890");

        var tomatoes = Product.Create(
            shop.Id,
            "VEG-TOM",
            "Tomatoes",
            category.Id,
            UnitOfMeasure.Kilogram,
            Rupees(450.00m),
            Rupees(270.00m),
            standardRate.Id);

        var rice = Product.Create(
            shop.Id,
            "GRN-RICE",
            "Red raw rice 5kg",
            category.Id,
            UnitOfMeasure.Pack,
            Rupees(1_250.00m),
            Rupees(1_050.00m),
            standardRate.Id);

        var shift = Shift.Open(shop.Id, terminal.Id, cashier.Id, Rupees(5_000.00m), Now);

        return new SampleShop(
            shop, terminal, cashier, manager, category, standardRate, tinnedFish, tomatoes, rice, shift);
    }

    /// <summary>
    /// A basket that touches everything the money model has to get right: whole units, a
    /// weighed line, an exempt line, a line discount, a whole-sale discount, and a split
    /// tender that ends in rounded cash.
    /// </summary>
    public Sale RingUpMixedBasket()
    {
        var sale = Sale.Start(Shop.Id, Terminal.Id, Shift.Id, Cashier.Id, Currency, Now);

        var fish = sale.AddLine(TinnedFish, Quantity.Of(3), StandardVat);
        sale.AddLine(Tomatoes, Quantity.Of(0.285m), StandardVat);
        sale.AddLine(Rice, Quantity.Of(1), Exempt);

        // SAL-07, within the cashier's own limit so no override is needed.
        sale.ApplyLineDiscount(
            fish.Id,
            Discount.OfPercentage(0.05m, Currency, "Dented tin"),
            Now);

        // SAL-08, apportioned across the three lines.
        sale.ApplySaleDiscount(Discount.OfAmount(Rupees(50.00m), "Regular customer"), Now);

        return sale;
    }
}
