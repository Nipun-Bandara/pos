using RetailCore.Domain.Common;
using RetailCore.Domain.Monetary;

namespace RetailCore.Domain.Tests;

public class QuantitySpecs
{
    [Fact]
    public void Weighed_goods_carry_three_decimal_places()
    {
        // SAL-05. One gram, when the unit is a kilogram.
        Quantity.Of(0.285m).Value.ShouldBe(0.285m);
    }

    [Fact]
    public void Precision_beyond_three_places_is_refused_not_rounded()
    {
        // Silently turning 0.2857 into 0.286 would make the receipt disagree with the
        // scale display the customer is looking at.
        Should.Throw<DomainException>(() => Quantity.Of(0.2857m));
    }

    [Fact]
    public void A_computed_quantity_may_be_rounded_deliberately()
    {
        Quantity.Rounded(0.2857m).Value.ShouldBe(0.286m);
    }

    [Fact]
    public void Negative_quantities_do_not_exist()
    {
        Should.Throw<DomainException>(() => Quantity.Of(-1m));
    }

    [Fact]
    public void Subtracting_past_zero_is_refused()
    {
        Should.Throw<DomainException>(() => Quantity.Of(1m) - Quantity.Of(2m));
    }

    [Fact]
    public void Whole_units_are_recognised_so_the_receipt_can_omit_the_decimals()
    {
        Quantity.Of(3).IsWholeUnits.ShouldBeTrue();
        Quantity.Of(0.5m).IsWholeUnits.ShouldBeFalse();

        Quantity.Of(3).ToString().ShouldBe("3");
        Quantity.Of(0.285m).ToString().ShouldBe("0.285");
    }
}
