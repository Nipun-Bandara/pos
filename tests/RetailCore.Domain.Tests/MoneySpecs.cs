using RetailCore.Domain.Common;
using RetailCore.Domain.Monetary;

namespace RetailCore.Domain.Tests;

/// <summary>
/// The money type is the foundation everything else stands on. These are specifications,
/// not coverage: each one states a rule the business depends on.
/// </summary>
public class MoneySpecs
{
    [Fact]
    public void Adding_amounts_is_exact()
    {
        var a = Money.FromMajor(0.10m, Currency.USD);
        var b = Money.FromMajor(0.20m, Currency.USD);

        (a + b).ShouldBe(Money.FromMajor(0.30m, Currency.USD));
    }

    [Fact]
    public void A_hundred_additions_of_a_cent_make_exactly_one_unit()
    {
        // The same loop with doubles lands on 1.0000000000000007.
        var total = Money.Zero(Currency.USD);
        for (var i = 0; i < 100; i++)
        {
            total += Money.FromMinor(1, Currency.USD);
        }

        total.ShouldBe(Money.FromMajor(1.00m, Currency.USD));
        total.MinorUnits.ShouldBe(100);
    }

    [Fact]
    public void Mixing_currencies_is_refused_rather_than_guessed_at()
    {
        var rupees = Money.FromMajor(100m, Currency.LKR);
        var dollars = Money.FromMajor(100m, Currency.USD);

        Should.Throw<DomainException>(() => rupees + dollars)
            .Message.ShouldContain("LKR");
    }

    [Fact]
    public void An_amount_without_a_currency_cannot_be_used()
    {
        var uninitialised = default(Money);

        Should.Throw<DomainException>(() => uninitialised + Money.FromMajor(1m, Currency.USD));
    }

    [Fact]
    public void Building_from_a_major_amount_rejects_precision_the_currency_cannot_hold()
    {
        // Half a cent is not a price. Rejecting it here stops it becoming a rounding
        // difference that only shows up when the drawer is counted.
        Should.Throw<DomainException>(() => Money.FromMajor(1.005m, Currency.USD));
    }

    [Fact]
    public void Zero_decimal_currencies_work_the_same_way()
    {
        var yen = Money.FromMajor(1500m, Currency.JPY);

        yen.MinorUnits.ShouldBe(1500);
        yen.ToDecimal().ShouldBe(1500m);
        Should.Throw<DomainException>(() => Money.FromMajor(1500.5m, Currency.JPY));
    }

    public class CashRounding
    {
        [Theory]
        // Sri Lanka: cents are quoted, but the smallest coin is one rupee. PAY-02.
        [InlineData(24960, 25000)] // 249.60 rounds up to 250.00
        [InlineData(24940, 24900)] // 249.40 rounds down to 249.00
        [InlineData(24950, 25000)] // exactly half a rupee rounds away from zero
        [InlineData(25000, 25000)] // already a whole rupee
        public void Rounds_to_the_smallest_coin_in_circulation(long givenMinor, long expectedMinor)
        {
            var amount = Money.FromMinor(givenMinor, Currency.LKR);

            amount.RoundToCashIncrement().ShouldBe(Money.FromMinor(expectedMinor, Currency.LKR));
        }

        [Fact]
        public void Leaves_currencies_whose_smallest_coin_is_the_minor_unit_alone()
        {
            var amount = Money.FromMajor(249.63m, Currency.USD);

            amount.RoundToCashIncrement().ShouldBe(amount);
        }
    }

    public class Allocation
    {
        [Fact]
        public void Splitting_an_indivisible_amount_loses_nothing()
        {
            // A hundred cents across three equal lines cannot divide evenly. Every scheme
            // that rounds each share independently loses or invents a cent here.
            var amount = Money.FromMajor(1.00m, Currency.USD);

            var shares = amount.Allocate([1, 1, 1]);

            shares.Select(s => s.MinorUnits).ShouldBe([34, 33, 33]);
            Money.Sum(shares, Currency.USD).ShouldBe(amount);
        }

        [Fact]
        public void Shares_follow_the_weights()
        {
            var amount = Money.FromMajor(10.00m, Currency.USD);

            var shares = amount.Allocate([700, 300]);

            shares[0].ShouldBe(Money.FromMajor(7.00m, Currency.USD));
            shares[1].ShouldBe(Money.FromMajor(3.00m, Currency.USD));
        }

        [Fact]
        public void A_zero_weight_gets_nothing()
        {
            var amount = Money.FromMajor(5.00m, Currency.USD);

            var shares = amount.Allocate([1, 0, 1]);

            shares[1].ShouldBe(Money.Zero(Currency.USD));
            Money.Sum(shares, Currency.USD).ShouldBe(amount);
        }

        [Fact]
        public void Negative_amounts_allocate_without_losing_a_unit_either()
        {
            var amount = Money.FromMajor(-1.00m, Currency.USD);

            var shares = amount.Allocate([1, 1, 1]);

            Money.Sum(shares, Currency.USD).ShouldBe(amount);
        }

        [Fact]
        public void Allocating_across_nothing_is_refused()
        {
            var amount = Money.FromMajor(1.00m, Currency.USD);

            Should.Throw<DomainException>(() => amount.Allocate([]));
            Should.Throw<DomainException>(() => amount.Allocate([0, 0]));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(7)]
        [InlineData(99)]
        [InlineData(1234)]
        [InlineData(100001)]
        public void Whatever_the_amount_the_shares_always_sum_back_to_it(long minorUnits)
        {
            var amount = Money.FromMinor(minorUnits, Currency.USD);

            var shares = amount.Allocate([3, 5, 7, 11, 13]);

            Money.Sum(shares, Currency.USD).ShouldBe(amount);
        }
    }

    public class Extension
    {
        [Fact]
        public void A_weighed_line_rounds_once_to_a_whole_minor_unit()
        {
            // 285 grams of something at 450.00 per kilo.
            var unitPrice = Money.FromMajor(450.00m, Currency.LKR);

            var lineAmount = unitPrice.Extend(Quantity.Of(0.285m));

            // 450.00 x 0.285 = 128.25 exactly.
            lineAmount.ShouldBe(Money.FromMajor(128.25m, Currency.LKR));
        }

        [Fact]
        public void A_fraction_of_a_minor_unit_rounds_away_from_zero()
        {
            var unitPrice = Money.FromMajor(9.99m, Currency.USD);

            // 9.99 x 0.115 = 1.14885, which is 1.15 to the cent.
            unitPrice.Extend(Quantity.Of(0.115m)).ShouldBe(Money.FromMajor(1.15m, Currency.USD));
        }
    }
}
