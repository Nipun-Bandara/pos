using RetailCore.Domain.Common;
using RetailCore.Domain.Monetary;
using RetailCore.Domain.Sales;

namespace RetailCore.Domain.Tests;

public class RefundSpecs
{
    private readonly TestShop _shop = new();

    [Fact]
    public void A_refund_gives_back_what_was_charged()
    {
        // RET-01.
        var sale = SaleOf(100.00m, quantity: 2);
        var line = sale.Lines[0];

        var refund = StartRefund(sale);
        refund.AddLine(line.Id, Quantity.Of(2));

        refund.Total.ShouldBe(_shop.Rupees(200.00m));
    }

    [Fact]
    public void Part_of_a_line_can_be_returned()
    {
        // RET-02.
        var sale = SaleOf(100.00m, quantity: 3);

        var refund = StartRefund(sale);
        refund.AddLine(sale.Lines[0].Id, Quantity.Of(1));

        refund.Total.ShouldBe(_shop.Rupees(100.00m));
    }

    [Fact]
    public void A_refund_cannot_exceed_what_was_sold()
    {
        // RET-03.
        var sale = SaleOf(100.00m, quantity: 2);

        var refund = StartRefund(sale);

        Should.Throw<DomainException>(() => refund.AddLine(sale.Lines[0].Id, Quantity.Of(3)))
            .Message.ShouldContain("left to refund");
    }

    [Fact]
    public void Earlier_refunds_count_against_what_is_left()
    {
        // RET-03 across visits. Two separate refunds of two units each against a sale of
        // three must not be possible just because neither one exceeds the sale on its own.
        var sale = SaleOf(100.00m, quantity: 3);
        var lineId = sale.Lines[0].Id;

        var second = Refund.Against(
            sale,
            _shop.ShiftId,
            _shop.CashierId,
            "Second visit",
            _shop.ManagerId,
            PaymentMethod.Cash,
            TestShop.Now,
            new Dictionary<SaleLineId, Quantity> { [lineId] = Quantity.Of(2) },
            alreadyRefundedAmounts: new Dictionary<SaleLineId,long> { [lineId] = 20000 });

        Should.Throw<DomainException>(() => second.AddLine(lineId, Quantity.Of(2)));

        second.AddLine(lineId, Quantity.Of(1));
        second.Total.ShouldBe(_shop.Rupees(100.00m));
    }

    [Fact]
    public void A_discounted_line_refunds_the_discounted_amount()
    {
        // Otherwise a 20% discount followed by a return is a way to walk cash out of the
        // shop, one item at a time.
        var sale = _shop.StartSale();
        var line = sale.AddLine(_shop.Item(100.00m), Quantity.Of(1), _shop.StandardVat);
        sale.ApplyLineDiscount(
            line.Id,
            Discount.OfPercentage(0.20m, TestShop.Currency, "Damaged", _shop.ManagerId),
            TestShop.Now);
        sale.AddCashPayment(_shop.Rupees(80.00m), TestShop.Now);
        sale.Complete(TestShop.NextReceipt(), TestShop.Now);

        var refund = StartRefund(sale);
        refund.AddLine(line.Id, Quantity.Of(1));

        refund.Total.ShouldBe(_shop.Rupees(80.00m));
    }

    [Fact]
    public void Refunding_a_line_in_pieces_gives_back_exactly_what_was_paid()
    {
        // The line is 9.99 and cash collected is 10.00 after rounding. Three returns
        // must reverse the actual 10.00 collected, using recorded prior amounts.
        var sale = _shop.StartSale();
        var line = sale.AddLine(_shop.Item(3.33m), Quantity.Of(3), _shop.StandardVat);
        sale.AddCashPayment(_shop.Rupees(10.00m), TestShop.Now);
        sale.Complete(TestShop.NextReceipt(), TestShop.Now);

        var charged = sale.AmountSettled;
        var refundedSoFar = Money.Zero(TestShop.Currency);
        var quantityRefunded = Quantity.Zero;

        for (var i = 0; i < 3; i++)
        {
            var refund = Refund.Against(
                sale,
                _shop.ShiftId,
                _shop.CashierId,
                "Returned one at a time",
                _shop.ManagerId,
                PaymentMethod.Cash,
                TestShop.Now,
                new Dictionary<SaleLineId, Quantity> { [line.Id] = quantityRefunded },
                alreadyRefundedAmounts: new Dictionary<SaleLineId,long> { [line.Id] = refundedSoFar.MinorUnits });

            refund.AddLine(line.Id, Quantity.One);
            refundedSoFar += refund.Total;
            quantityRefunded += Quantity.One;
        }

        refundedSoFar.ShouldBe(charged);
    }

    [Fact]
    public void Tax_comes_back_at_the_rate_that_was_charged()
    {
        // CMP-02. A rate change between the sale and the return must not alter the refund.
        var sale = SaleOf(118.00m, quantity: 1);

        var refund = StartRefund(sale);
        refund.AddLine(sale.Lines[0].Id, Quantity.Of(1));

        refund.Total.ShouldBe(_shop.Rupees(118.00m));
        refund.TaxTotal.ShouldBe(_shop.Rupees(18.00m));
    }

    [Fact]
    public void An_incomplete_sale_cannot_be_refunded()
    {
        var sale = _shop.StartSale();
        sale.AddLine(_shop.Item(100.00m), Quantity.Of(1), _shop.StandardVat);

        Should.Throw<DomainException>(() => StartRefund(sale));
    }

    [Fact]
    public void The_same_line_cannot_be_added_twice_to_one_refund()
    {
        var sale = SaleOf(100.00m, quantity: 3);
        var refund = StartRefund(sale);
        refund.AddLine(sale.Lines[0].Id, Quantity.Of(1));

        Should.Throw<DomainException>(() => refund.AddLine(sale.Lines[0].Id, Quantity.Of(1)));
    }

    [Fact]
    public void Completing_a_refund_announces_the_stock_coming_back()
    {
        // RET-06.
        var sale = SaleOf(100.00m, quantity: 2);
        var refund = StartRefund(sale);
        refund.AddLine(sale.Lines[0].Id, Quantity.Of(2));

        refund.Complete(TestShop.NextReceipt(2), TestShop.Now);

        var issued = refund.DomainEvents.OfType<RefundIssued>().Single();
        issued.Items.ShouldHaveSingleItem();
        issued.Items[0].Quantity.ShouldBe(Quantity.Of(2));
        issued.AuthorisedBy.ShouldBe(_shop.ManagerId);
        issued.Total.ShouldBe(_shop.Rupees(200.00m));
    }

    [Fact]
    public void A_completed_refund_cannot_be_changed()
    {
        // CMP-04.
        var sale = SaleOf(100.00m, quantity: 2);
        var refund = StartRefund(sale);
        refund.AddLine(sale.Lines[0].Id, Quantity.Of(1));
        refund.Complete(TestShop.NextReceipt(2), TestShop.Now);

        Should.Throw<DomainException>(() => refund.RemoveLine(refund.Lines[0].Id));
    }

    [Fact]
    public void An_empty_refund_cannot_be_completed()
    {
        var sale = SaleOf(100.00m, quantity: 1);
        var refund = StartRefund(sale);

        Should.Throw<DomainException>(() => refund.Complete(TestShop.NextReceipt(2), TestShop.Now));
    }

    private Sale SaleOf(decimal price, int quantity)
    {
        var sale = _shop.StartSale();
        sale.AddLine(_shop.Item(price), Quantity.Of(quantity), _shop.StandardVat);
        sale.AddCashPayment(_shop.Rupees(price * quantity), TestShop.Now);
        sale.Complete(TestShop.NextReceipt(), TestShop.Now);
        return sale;
    }

    private Refund StartRefund(Sale sale) => Refund.Against(
        sale,
        _shop.ShiftId,
        _shop.CashierId,
        "Customer returned it",
        _shop.ManagerId,
        PaymentMethod.Cash,
        TestShop.Now);
}
