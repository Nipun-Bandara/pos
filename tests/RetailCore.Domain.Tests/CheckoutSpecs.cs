using RetailCore.Domain.Catalogue;
using RetailCore.Domain.Common;
using RetailCore.Domain.Monetary;
using RetailCore.Domain.Sales;
using RetailCore.Domain.Tax;

namespace RetailCore.Domain.Tests;

public sealed class CheckoutSpecs
{
    private static readonly Currency Lkr = Currency.FromCode("LKR");
    private static Product Product(decimal price = 100, UnitOfMeasure unit = UnitOfMeasure.Each)
    {
        var p = RetailCore.Domain.Catalogue.Product.Create(ShopId.New(), "TEA", "Tea", CategoryId.New(), unit,
            Money.FromMajor(price, Lkr), Money.FromMajor(50, Lkr), TaxRateId.New());
        p.ConfigureCatalogue("TEA", "තේ", "Tea", "තේ පැකට්", p.CategoryId, unit, p.UnitCost, p.UnitPrice, "001234", [], true);
        return p;
    }
    private static Sale Sale(Product p) => RetailCore.Domain.Sales.Sale.Start(p.ShopId, TerminalId.New(), ShiftId.New(), UserId.New(), Lkr, DateTimeOffset.UtcNow);

    [Fact]
    public void Configured_cash_increment_rounds_separately_and_payment_amount_excludes_change()
    {
        var p = Product(150.26m); var sale = Sale(p);
        sale.AddCheckoutLine(p, Quantity.One, TaxTreatment.Exempt("NO-TAX"));
        sale.AddCashPayment(Money.FromMajor(200, Lkr), DateTimeOffset.UtcNow, 50).ToDecimal().ShouldBe(49.50m);
        sale.Total.ToDecimal().ShouldBe(150.26m);
        sale.CashRoundingAdjustment.ToDecimal().ShouldBe(.24m);
        sale.AmountSettled.ToDecimal().ShouldBe(150.50m);
        sale.Payments.Single().Amount.ToDecimal().ShouldBe(150.50m);
        sale.BalanceDue.IsZero.ShouldBeTrue();
        Money.FromMajor(150.25m, Lkr).RoundToIncrement(50).ToDecimal().ShouldBe(150.50m);
        Money.FromMajor(150.24m, Lkr).RoundToIncrement(50).ToDecimal().ShouldBe(150m);
    }

    [Fact]
    public void Cash_rounding_down_to_zero_can_settle_a_small_positive_total_without_inventing_payment()
    {
        var p = Product(.01m); var sale = Sale(p);
        sale.AddCheckoutLine(p, Quantity.One, TaxTreatment.Exempt("NO-TAX"));
        sale.AddCashPayment(Money.Zero(Lkr), DateTimeOffset.UtcNow, 100).IsZero.ShouldBeTrue();
        sale.Complete(ReceiptNumber.Create("T1", 1), DateTimeOffset.UtcNow);
        sale.Total.MinorUnits.ShouldBe(1); sale.CashRoundingAdjustment.MinorUnits.ShouldBe(-1);
        sale.Payments.Single().Amount.MinorUnits.ShouldBe(0);
        Should.Throw<DomainException>(() => Money.FromMajor(100, Lkr).RoundToIncrement(0));
    }

    [Fact]
    public void Manual_price_replaces_tier_and_keeps_both_prices_reason_and_authorizer()
    {
        var p = Product();
        p.ConfigureCatalogue(p.Sku, p.SinhalaName, p.Name, p.ReceiptName, p.CategoryId, p.Unit, p.UnitCost, p.UnitPrice, "001234",
            [new QuantityPriceBand(Quantity.Of(2), Money.FromMajor(90, Lkr))], true);
        var sale = Sale(p); var owner = UserId.New();
        var line = sale.AddCheckoutLine(p, Quantity.Of(2), TaxTreatment.Exempt("NO-TAX"), Money.FromMajor(80, Lkr), "Owner offer", owner);
        line.LineTotal.ToDecimal().ShouldBe(160);
        line.UnitPrice.ToDecimal().ShouldBe(80);
        line.NormalUnitPriceMinor.ShouldBe(10000); line.QualifiedTierUnitPriceMinor.ShouldBe(9000);
        line.QualifiedPriceBand.ShouldBe(1); line.AppliedPriceBand.ShouldBeNull();
        line.ManualPriceApprovedBy.ShouldBe(owner); line.ManualPriceReason.ShouldBe("Owner offer");
        line.ReceiptNameSnapshot.ShouldBe("තේ පැකට්");
        Should.Throw<DomainException>(() => Sale(p).AddCheckoutLine(p, Quantity.Of(2), TaxTreatment.Exempt("NO-TAX"), Money.FromMajor(91, Lkr), "Offer", owner));
        Should.Throw<DomainException>(() => Sale(p).AddCheckoutLine(p, Quantity.Of(2), TaxTreatment.Exempt("NO-TAX"), Money.FromMajor(80, Lkr), "Offer"));
    }

    [Fact]
    public void Summing_checkout_money_never_wraps_into_a_negative_balance()
    {
        var maximum = Money.FromMinor(long.MaxValue, Lkr);
        Should.Throw<OverflowException>(() => Money.Sum([maximum, Money.FromMinor(1, Lkr)], Lkr));
        Should.Throw<OverflowException>(() => maximum.MultiplyBy(2L));
        Should.Throw<OverflowException>(() => maximum.RoundToIncrement(10));
    }

    [Theory]
    [InlineData("4111111111111111")]
    [InlineData("4111-1111-1111-1111")]
    [InlineData("123")]
    [InlineData("PIN-1234")]
    [InlineData("CVV123")]
    public void Card_reference_cannot_be_numeric_card_or_credential_data(string reference)
    {
        var p = Product(); var sale = Sale(p); sale.AddCheckoutLine(p, Quantity.One, TaxTreatment.Exempt("NO-TAX"));
        Should.Throw<DomainException>(() => sale.AddPayment(PaymentMethod.Card, sale.Total, reference, DateTimeOffset.UtcNow));
        sale.Payments.ShouldBeEmpty();
    }
}
