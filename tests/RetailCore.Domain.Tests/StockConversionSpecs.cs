using RetailCore.Domain.Catalogue;
using RetailCore.Domain.Common;
using RetailCore.Domain.Inventory;
using RetailCore.Domain.Monetary;

namespace RetailCore.Domain.Tests;

public sealed class StockConversionSpecs
{
    [Fact]
    public void Two_bags_convert_to_fifty_kg_and_mixed_consumption_leaves_twenty_three_point_seven_five()
    {
        StockConversion.ToBase(Quantity.Of(2), 25000).ShouldBe(Quantity.Of(50));
        var remaining = StockConversion.After(50000,
            [(StockConversion.ToBase(Quantity.Of(1.250m), 1000), StockDirection.Out),
             (StockConversion.ToBase(Quantity.One, 25000), StockDirection.Out)]);
        remaining.ShouldBe(23750);
    }

    [Fact]
    public void Conversion_rejects_zero_negative_excess_precision_and_overflow()
    {
        Should.Throw<DomainException>(() => StockConversion.ToBase(Quantity.One, 0));
        Should.Throw<DomainException>(() => StockConversion.ToBase(Quantity.One, -1));
        Should.Throw<DomainException>(() => StockConversion.ToBase(Quantity.Zero, 1000));
        Should.Throw<DomainException>(() => StockConversion.ToBase(Quantity.Of(.001m), 1));
        Should.Throw<OverflowException>(() => StockConversion.ToBase(Quantity.FromThousandths(long.MaxValue), 2000));
        Should.Throw<OverflowException>(() => StockConversion.After(long.MaxValue, [(Quantity.One, StockDirection.In)]));
    }

    [Fact]
    public void Posted_movement_keeps_entered_quantity_factor_and_both_costs()
    {
        var shop = ShopId.New(); var pack = ProductId.New(); var root = ProductId.New(); var actor = UserId.New();
        var currency = Currency.FromCode("LKR");
        var movement = StockMovement.Posted(shop, pack, root, Quantity.Of(2), 25000, "Kilogram", StockDirection.In,
            StockMovementKind.GoodsReceived, "StockEntry", Guid.NewGuid(), DateTimeOffset.UtcNow, actor,
            "Delivery", Money.FromMajor(2500, currency), reference: "Supplier 1");
        movement.ProductId.ShouldBe(root);
        movement.EnteredProductId.ShouldBe(pack);
        movement.InputQuantityThousandths.ShouldBe(2000);
        movement.ConversionFactorThousandths.ShouldBe(25000);
        movement.Quantity.Thousandths.ShouldBe(50000);
        movement.UnitCost!.Value.ToDecimal().ShouldBe(100);
        movement.InputUnitCostMinor.ShouldBe(250000);
        movement.PostingKey.ShouldNotBeNull();
    }

    [Fact]
    public void Discrete_base_units_require_whole_conversion_factors()
    {
        var currency = Currency.FromCode("LKR");
        var p = Product.Create(ShopId.New(), "PACK", "Pack", CategoryId.New(), UnitOfMeasure.Pack,
            Money.FromMajor(100, currency), Money.Zero(currency), TaxRateId.New());
        Should.Throw<DomainException>(() => p.ConfigureStock(ProductId.New(), UnitOfMeasure.Each, 1500, Quantity.Zero));
        p.ConfigureStock(ProductId.New(), UnitOfMeasure.Kilogram, 1250, Quantity.Zero);
        p.StockFactorThousandths.ShouldBe(1250);
    }
}
