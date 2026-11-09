using RetailCore.Domain.Common;
using RetailCore.Domain.Monetary;

namespace RetailCore.Domain.Catalogue;

public sealed class QuantityPriceBand
{
    private QuantityPriceBand() { }
    public QuantityPriceBand(Quantity minimum, Money price)
    {
        MinimumThousandths = minimum.Thousandths;
        PriceMinor = price.MinorUnits;
        CurrencyCode = price.CurrencyCode;
    }
    public long MinimumThousandths { get; private set; }
    public long PriceMinor { get; private set; }
    public string CurrencyCode { get; private set; } = "";
}

public sealed record PriceQuote(Money UnitPrice, Money Total, Money Savings,
    int Band, long MinimumThousandths, string SinhalaName, string EnglishName, string ReceiptName);

public static class QuantityPricing
{
    public static void Validate(Money normal, UnitOfMeasure unit, IReadOnlyList<QuantityPriceBand> bands)
    {
        if (!normal.IsPositive || !Enum.IsDefined(unit) || bands.Count > 3)
            throw new DomainException("InvalidPricing");
        long previous = 0;
        var price = normal;
        foreach (var band in bands)
        {
            var next = Money.FromMinor(band.PriceMinor, Currency.FromCode(band.CurrencyCode));
            if (band.MinimumThousandths <= previous || band.MinimumThousandths <= 0
                || (!unit.AllowsFractionalQuantity() && band.MinimumThousandths % 1000 != 0)
                || next.Currency != normal.Currency || !next.IsPositive || next > price)
                throw new DomainException("InvalidPricing");
            previous = band.MinimumThousandths;
            price = next;
        }
    }

    public static PriceQuote Preview(Product product, Quantity quantity)
    {
        ArgumentNullException.ThrowIfNull(product);
        Validate(product.UnitPrice, product.Unit, product.PriceBands);
        if (!quantity.IsPositive || (!product.Unit.AllowsFractionalQuantity() && !quantity.IsWholeUnits))
            throw new DomainException("InvalidQuantity");
        var price = product.UnitPrice;
        var index = 0;
        long minimum = 0;
        foreach (var band in product.PriceBands)
        {
            if (quantity.Thousandths < band.MinimumThousandths) break;
            index++;
            minimum = band.MinimumThousandths;
            price = Money.FromMinor(band.PriceMinor, product.UnitPrice.Currency);
        }
        var total = price.Extend(quantity);
        return new PriceQuote(price, total, product.UnitPrice.Extend(quantity) - total,
            index, minimum, product.SinhalaName, product.Name, product.ReceiptName);
    }
}
