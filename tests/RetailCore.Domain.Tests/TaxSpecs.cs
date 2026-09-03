using RetailCore.Domain.Common;
using RetailCore.Domain.Monetary;
using RetailCore.Domain.Tax;

namespace RetailCore.Domain.Tests;

public class TaxSpecs
{
    private readonly TestShop _shop = new();

    [Fact]
    public void Tax_is_extracted_from_a_tax_inclusive_price_not_added_to_it()
    {
        // CMP-01. A shelf price of 118.00 at 18% contains 18.00 of tax, leaving 100.00 net.
        // Adding instead of extracting would give 21.24, a 3.24 overstatement on this line.
        var gross = _shop.Rupees(118.00m);

        _shop.StandardVat.TaxWithin(gross).ShouldBe(_shop.Rupees(18.00m));
        _shop.StandardVat.NetWithin(gross).ShouldBe(_shop.Rupees(100.00m));
    }

    [Fact]
    public void The_net_and_the_tax_always_add_back_to_the_gross()
    {
        // Whatever the rounding does, the customer's total must not move.
        foreach (var minor in new long[] { 1, 7, 99, 12345, 999999 })
        {
            var gross = Money.FromMinor(minor, TestShop.Currency);

            (_shop.StandardVat.NetWithin(gross) + _shop.StandardVat.TaxWithin(gross)).ShouldBe(gross);
        }
    }

    [Fact]
    public void Zero_rated_goods_carry_no_tax()
    {
        // CMP-03.
        var gross = _shop.Rupees(250.00m);

        _shop.ZeroRated.TaxWithin(gross).ShouldBe(Money.Zero(TestShop.Currency));
        _shop.ZeroRated.NetWithin(gross).ShouldBe(gross);
    }

    [Fact]
    public void Exempt_goods_carry_no_tax_but_are_a_different_category()
    {
        // CMP-03. Both collect nothing; only the category tells the tax summary (BO-09)
        // which bucket the turnover belongs in.
        var gross = _shop.Rupees(250.00m);

        _shop.Exempt.TaxWithin(gross).ShouldBe(Money.Zero(TestShop.Currency));
        _shop.Exempt.Category.ShouldBe(TaxCategory.Exempt);
        _shop.ZeroRated.Category.ShouldBe(TaxCategory.ZeroRated);
    }

    [Fact]
    public void An_implausible_rate_is_refused()
    {
        // Catches the classic mistake of passing 18 where 0.18 was meant.
        Should.Throw<DomainException>(() => TaxTreatment.Standard("VAT18", 18m));
    }

    public class EffectiveDating
    {
        private readonly TestShop _shop = new();

        [Fact]
        public void A_rate_applies_only_within_its_period()
        {
            // CMP-02. The rate is data with dates, so a budget change is entered in advance
            // and starts applying on its own, without a release on the night.
            var rate = TaxRate.Create(
                _shop.ShopId,
                "VAT15",
                "VAT at 15%",
                TaxCategory.Standard,
                0.15m,
                new DateOnly(2026, 1, 1),
                new DateOnly(2026, 3, 31));

            rate.IsEffectiveOn(new DateOnly(2025, 12, 31)).ShouldBeFalse();
            rate.IsEffectiveOn(new DateOnly(2026, 1, 1)).ShouldBeTrue();
            rate.IsEffectiveOn(new DateOnly(2026, 3, 31)).ShouldBeTrue();
            rate.IsEffectiveOn(new DateOnly(2026, 4, 1)).ShouldBeFalse();
        }

        [Fact]
        public void An_open_ended_rate_has_no_end()
        {
            var rate = TaxRate.Create(
                _shop.ShopId,
                "VAT18",
                "VAT at 18%",
                TaxCategory.Standard,
                0.18m,
                new DateOnly(2026, 4, 1));

            rate.IsEffectiveOn(new DateOnly(2030, 1, 1)).ShouldBeTrue();

            rate.EndOn(new DateOnly(2027, 12, 31));

            rate.IsEffectiveOn(new DateOnly(2030, 1, 1)).ShouldBeFalse();
        }

        [Fact]
        public void A_period_cannot_end_before_it_starts()
        {
            Should.Throw<DomainException>(() => TaxRate.Create(
                _shop.ShopId,
                "VAT18",
                "VAT at 18%",
                TaxCategory.Standard,
                0.18m,
                new DateOnly(2026, 4, 1),
                new DateOnly(2026, 1, 1)));
        }

        [Fact]
        public void An_exempt_rate_cannot_carry_a_percentage()
        {
            Should.Throw<DomainException>(() => TaxRate.Create(
                _shop.ShopId,
                "EXEMPT",
                "Exempt",
                TaxCategory.Exempt,
                0.18m,
                new DateOnly(2026, 1, 1)));
        }

        [Fact]
        public void A_rate_snapshots_onto_a_line_as_a_treatment()
        {
            var rate = TaxRate.Create(
                _shop.ShopId,
                "VAT18",
                "VAT at 18%",
                TaxCategory.Standard,
                0.18m,
                new DateOnly(2026, 4, 1));

            var treatment = rate.ToTreatment();

            treatment.Code.ShouldBe("VAT18");
            treatment.Rate.ShouldBe(0.18m);
            treatment.TaxWithin(Money.FromMajor(118m, TestShop.Currency))
                .ShouldBe(Money.FromMajor(18m, TestShop.Currency));
        }
    }
}
