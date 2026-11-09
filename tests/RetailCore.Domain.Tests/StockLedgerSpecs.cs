using RetailCore.Domain.Common;
using RetailCore.Domain.Inventory;
using RetailCore.Domain.Monetary;

namespace RetailCore.Domain.Tests;

public class StockLedgerSpecs
{
    private readonly TestShop _shop = new();
    private readonly ProductId _product = ProductId.New();

    [Fact]
    public void Stock_on_hand_is_the_sum_of_the_ledger()
    {
        // INV-01 and INV-02. There is no quantity column to disagree with this.
        var ledger = new List<StockMovement>
        {
            Received(100),
            Sold(30),
            Sold(5),
            Returned(2),
        };

        ledger.Sum(m => m.SignedQuantity).ShouldBe(67m);
    }

    [Fact]
    public void Selling_takes_stock_out()
    {
        var movement = Sold(3);

        movement.Direction.ShouldBe(StockDirection.Out);
        movement.Kind.ShouldBe(StockMovementKind.Sale);
        movement.SignedQuantity.ShouldBe(-3m);
    }

    [Fact]
    public void A_refund_puts_it_back()
    {
        // RET-06.
        var movement = Returned(3);

        movement.Direction.ShouldBe(StockDirection.In);
        movement.SignedQuantity.ShouldBe(3m);
    }

    [Fact]
    public void A_movement_traces_back_to_what_caused_it()
    {
        var saleId = SaleId.New();

        var movement = StockMovement.ForSale(
            _shop.ShopId, _product, Quantity.Of(1), saleId, TestShop.Now);

        movement.SourceType.ShouldBe("Sale");
        movement.SourceId.ShouldBe(saleId.Value);
    }

    [Fact]
    public void Ledger_representation_preserves_negative_balances()
    {
        // Signed history represents an approved negative balance. Application posting
        // separately requires a reason and owner authorization before creating one.
        var ledger = new List<StockMovement> { Received(1), Sold(3) };

        ledger.Sum(m => m.SignedQuantity).ShouldBe(-2m);
    }

    [Fact]
    public void A_manual_adjustment_must_carry_a_reason()
    {
        // INV-05 and INV-06.
        Should.Throw<ArgumentException>(() => StockMovement.Manual(
            _shop.ShopId,
            _product,
            Quantity.Of(1),
            StockDirection.Out,
            StockMovementKind.Wastage,
            "   ",
            _shop.ManagerId,
            TestShop.Now));
    }

    [Fact]
    public void Wastage_is_recorded_with_who_and_why()
    {
        var movement = StockMovement.Manual(
            _shop.ShopId,
            _product,
            Quantity.Of(4),
            StockDirection.Out,
            StockMovementKind.Wastage,
            "Dropped a crate",
            _shop.ManagerId,
            TestShop.Now);

        movement.Reason.ShouldBe("Dropped a crate");
        movement.RecordedBy.ShouldBe(_shop.ManagerId);
        movement.SignedQuantity.ShouldBe(-4m);
    }

    [Fact]
    public void Sale_movements_cannot_be_entered_by_hand()
    {
        // INV-03: exactly one path from "goods left the shop" to "the ledger says so".
        Should.Throw<DomainException>(() => StockMovement.Manual(
            _shop.ShopId,
            _product,
            Quantity.Of(1),
            StockDirection.Out,
            StockMovementKind.Sale,
            "Trying to fake a sale",
            _shop.ManagerId,
            TestShop.Now));
    }

    [Fact]
    public void There_is_no_way_to_change_a_movement_once_written()
    {
        // CMP-04. A mistake is corrected with a compensating movement, not an edit.
        var mutators = typeof(StockMovement)
            .GetProperties()
            .Where(p => p.SetMethod?.IsPublic == true)
            .Select(p => p.Name);

        mutators.ShouldBeEmpty();
    }

    private StockMovement Received(int units) => StockMovement.Manual(
        _shop.ShopId,
        _product,
        Quantity.Of(units),
        StockDirection.In,
        StockMovementKind.GoodsReceived,
        "Delivery",
        _shop.ManagerId,
        TestShop.Now);

    private StockMovement Sold(int units) => StockMovement.ForSale(
        _shop.ShopId, _product, Quantity.Of(units), SaleId.New(), TestShop.Now);

    private StockMovement Returned(int units) => StockMovement.ForRefund(
        _shop.ShopId, _product, Quantity.Of(units), RefundId.New(), TestShop.Now);
}
