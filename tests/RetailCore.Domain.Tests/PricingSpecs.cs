using RetailCore.Domain.Catalogue;
using RetailCore.Domain.Common;
using RetailCore.Domain.Monetary;
using RetailCore.Domain.Sales;
using RetailCore.Domain.Tax;

namespace RetailCore.Domain.Tests;

public sealed class PricingSpecs
{
    private static readonly Currency Lkr = Currency.FromCode("LKR");
    private static Money Price(decimal value) => Money.FromMajor(value, Lkr);
    private static QuantityPriceBand Band(decimal minimum, decimal price) => new(Quantity.Of(minimum), Price(price));
    private static Product Product(UnitOfMeasure unit = UnitOfMeasure.Each, params QuantityPriceBand[] bands)
    {
        var p = RetailCore.Domain.Catalogue.Product.Create(ShopId.New(), "TEA", "Tea", CategoryId.New(), unit,
            Price(100), Price(50), TaxRateId.New());
        p.ConfigureCatalogue("TEA", "තේ", "Tea", "තේ පැකට්", p.CategoryId, unit, Price(50), Price(100), "001234", bands, true);
        return p;
    }

    [Fact]
    public void Highest_qualifying_band_prices_every_unit_and_reducing_quantity_reselects()
    {
        var p = Product(UnitOfMeasure.Each, Band(2, 90), Band(3, 85), Band(4, 80));
        var totals = new[] { 1, 2, 3, 4, 5, 2 }.Select(q => QuantityPricing.Preview(p, Quantity.Of(q)).Total.ToDecimal());
        totals.ShouldBe(new decimal[] { 100, 180, 255, 320, 400, 180 });
        var quote = QuantityPricing.Preview(p, Quantity.Of(5));
        quote.Band.ShouldBe(3);
        quote.MinimumThousandths.ShouldBe(4000);
        quote.UnitPrice.ShouldBe(Price(80));
        quote.Savings.ShouldBe(Price(100));
    }

    [Fact]
    public void Fractional_thresholds_and_line_rounding_are_supported_for_weighted_goods()
    {
        var p = Product(UnitOfMeasure.Kilogram, Band(.250m, 90), Band(1.250m, 85), Band(2.500m, 80));
        QuantityPricing.Preview(p, Quantity.Of(.249m)).Total.ShouldBe(Price(24.90m));
        QuantityPricing.Preview(p, Quantity.Of(.250m)).Total.ShouldBe(Price(22.50m));
        QuantityPricing.Preview(p, Quantity.Of(1.250m)).Total.ShouldBe(Price(106.25m));
        QuantityPricing.Preview(p, Quantity.Of(2.500m)).Total.ShouldBe(Price(200));
        var rounding = Product(UnitOfMeasure.Kilogram, Band(.001m, .05m));
        QuantityPricing.Preview(rounding, Quantity.Of(.1m)).Total.MinorUnits.ShouldBe(1);
    }

    [Fact]
    public void Invalid_band_counts_order_currency_prices_and_discrete_thresholds_are_rejected()
    {
        QuantityPriceBand[][] invalid = [
            [Band(2, 90), Band(3, 85), Band(4, 80), Band(5, 75)],
            [Band(3, 90), Band(2, 85)], [Band(2, 90), Band(2, 85)],
            [Band(0, 90)], [Band(2, 101)], [Band(2, 0)], [Band(2, -1)],
            [Band(2, 90), Band(3, 95)], [Band(.5m, 90)],
            [new QuantityPriceBand(Quantity.Of(2), Money.FromMajor(90, Currency.FromCode("USD")))]
        ];
        foreach (var bands in invalid) Should.Throw<DomainException>(() => Product(UnitOfMeasure.Each, bands));
        Should.Throw<DomainException>(() => QuantityPricing.Preview(Product(), Quantity.Zero));
        Should.Throw<DomainException>(() => QuantityPricing.Preview(Product(), Quantity.Of(.5m)));
        Should.Throw<DomainException>(() => Quantity.Of(.0001m));
        QuantityPricing.Validate(Price(100), UnitOfMeasure.Each, [Band(2, 100)]);
    }

    [Fact]
    public void Quote_and_sale_snapshot_names_tier_and_price_do_not_follow_catalogue_edits()
    {
        var p = Product(UnitOfMeasure.Each, Band(2, 90));
        var sale = Sale.Start(p.ShopId, TerminalId.New(), ShiftId.New(), UserId.New(), Lkr, DateTimeOffset.UtcNow);
        var line = sale.AddPricedLine(p, Quantity.Of(2), TaxTreatment.Exempt("NO-TAX"));
        p.ConfigureCatalogue("NEW", "නව තේ", "New tea", "නව තේ", p.CategoryId, p.Unit,
            Price(70), Price(200), "009999", [Band(2, 150)], true);
        line.ReceiptNameSnapshot.ShouldBe("තේ පැකට්");
        line.SinhalaNameSnapshot.ShouldBe("තේ");
        line.EnglishNameSnapshot.ShouldBe("Tea");
        line.AppliedPriceBand.ShouldBe(1);
        line.AppliedMinimumThousandths.ShouldBe(2000);
        line.UnitPrice.ShouldBe(Price(90));
        line.UnitCost.ShouldBe(Price(50));
        line.LineTotal.ShouldBe(Price(180));
    }
}
