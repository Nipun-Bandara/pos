using RetailCore.Domain.Common;
using RetailCore.Domain.Monetary;
using RetailCore.Domain.Sales;

namespace RetailCore.Domain.Tests;

public class SaleSpecs
{
    private readonly TestShop _shop = new();

    public class Totals
    {
        private readonly TestShop _shop = new();

        [Fact]
        public void Are_derived_from_the_lines()
        {
            // SAL-10.
            var sale = _shop.StartSale();
            sale.AddLine(_shop.Item(118.00m), Quantity.Of(2), _shop.StandardVat);

            sale.GrossAmount.ShouldBe(_shop.Rupees(236.00m));
            sale.Total.ShouldBe(_shop.Rupees(236.00m));
            sale.TaxTotal.ShouldBe(_shop.Rupees(36.00m));
            sale.NetTotal.ShouldBe(_shop.Rupees(200.00m));
        }

        [Fact]
        public void Cannot_be_assigned_from_outside_the_aggregate()
        {
            // SAL-10 is a structural guarantee, not a convention. If someone adds a public
            // setter to make a mapper or a test easier, this fails.
            foreach (var name in new[] { "Total", "TaxTotal", "NetTotal", "GrossAmount", "DiscountTotal" })
            {
                var setter = typeof(Sale).GetProperty(name)?.SetMethod;

                setter.ShouldNotBeNull($"Sale.{name} should exist");
                setter.IsPublic.ShouldBeFalse($"Sale.{name} must not be publicly assignable");
            }
        }

        [Fact]
        public void Follow_the_lines_as_they_change()
        {
            var sale = _shop.StartSale();
            var line = sale.AddLine(_shop.Item(50.00m), Quantity.Of(1), _shop.StandardVat);

            sale.Total.ShouldBe(_shop.Rupees(50.00m));

            sale.ChangeLineQuantity(line.Id, Quantity.Of(3));

            sale.Total.ShouldBe(_shop.Rupees(150.00m));
        }

        [Fact]
        public void Mix_tax_treatments_correctly_across_lines()
        {
            // CMP-03. A basket with taxable and exempt goods in it is the normal case, not
            // an edge case, and the tax total must only reflect the taxable lines.
            var sale = _shop.StartSale();
            sale.AddLine(_shop.Item(118.00m, name: "Soap", sku: "SOAP"), Quantity.Of(1), _shop.StandardVat);
            sale.AddLine(_shop.Item(200.00m, name: "Rice", sku: "RICE"), Quantity.Of(1), _shop.Exempt);

            sale.Total.ShouldBe(_shop.Rupees(318.00m));
            sale.TaxTotal.ShouldBe(_shop.Rupees(18.00m));
            sale.NetTotal.ShouldBe(_shop.Rupees(300.00m));
        }

        [Fact]
        public void Include_the_cost_of_goods_for_the_margin_report()
        {
            // CAT-03 and BO-08, from the cost snapshotted onto the line.
            var sale = _shop.StartSale();
            sale.AddLine(_shop.Item(100.00m, cost: 60.00m), Quantity.Of(2), _shop.StandardVat);

            sale.CostOfGoods.ShouldBe(_shop.Rupees(120.00m));
        }
    }

    public class Lines
    {
        private readonly TestShop _shop = new();

        [Fact]
        public void Weighed_goods_extend_by_weight()
        {
            // SAL-05. 285 grams of tomatoes at 450.00 per kilo.
            var sale = _shop.StartSale();
            sale.AddLine(_shop.Weighed(450.00m), Quantity.Of(0.285m), _shop.StandardVat);

            sale.Total.ShouldBe(_shop.Rupees(128.25m));
        }

        [Fact]
        public void Goods_sold_by_the_item_reject_a_fractional_quantity()
        {
            var sale = _shop.StartSale();

            Should.Throw<DomainException>(() =>
                sale.AddLine(_shop.Item(100.00m), Quantity.Of(1.5m), _shop.StandardVat));
        }

        [Fact]
        public void A_voided_line_stops_counting_but_stays_on_the_record()
        {
            // SAL-06 with SEC-06: the row survives so the audit trail shows what was rung
            // up and taken off again, which is the pattern worth watching for.
            var sale = _shop.StartSale();
            var keep = sale.AddLine(_shop.Item(100.00m, sku: "A"), Quantity.Of(1), _shop.StandardVat);
            var drop = sale.AddLine(_shop.Item(60.00m, sku: "B"), Quantity.Of(1), _shop.StandardVat);

            sale.VoidLine(drop.Id, "Customer changed their mind", _shop.CashierId, TestShop.Now);

            sale.Total.ShouldBe(_shop.Rupees(100.00m));
            sale.Lines.Count.ShouldBe(2);
            sale.ActiveLines.Select(l => l.Id).ShouldBe([keep.Id]);
            sale.Lines.Single(l => l.Id == drop.Id).VoidReason.ShouldBe("Customer changed their mind");
            sale.DomainEvents.OfType<SaleLineVoided>().ShouldHaveSingleItem();
        }

        [Fact]
        public void An_open_price_item_needs_a_price_at_the_till()
        {
            // SAL-13.
            var sale = _shop.StartSale();
            var product = _shop.OpenPriced();

            Should.Throw<DomainException>(() =>
                sale.AddLine(product, Quantity.Of(1), _shop.StandardVat));

            sale.AddLine(product, Quantity.Of(1), _shop.StandardVat, _shop.Rupees(75.00m));

            sale.Total.ShouldBe(_shop.Rupees(75.00m));
        }

        [Fact]
        public void A_product_from_another_shop_cannot_be_sold_here()
        {
            // TEN-01, checked in the domain as well as by row level security.
            var otherShop = new TestShop();
            var sale = _shop.StartSale();

            Should.Throw<DomainException>(() =>
                sale.AddLine(otherShop.Item(100.00m), Quantity.Of(1), _shop.StandardVat));
        }

        [Fact]
        public void Price_and_cost_are_snapshotted_so_later_catalogue_changes_do_not_rewrite_history()
        {
            // CAT-05.
            var product = _shop.Item(100.00m, cost: 60.00m);
            var sale = _shop.StartSale();
            sale.AddLine(product, Quantity.Of(1), _shop.StandardVat);

            product.Reprice(_shop.Rupees(150.00m));
            product.RecordCost(_shop.Rupees(90.00m));

            sale.Total.ShouldBe(_shop.Rupees(100.00m));
            sale.CostOfGoods.ShouldBe(_shop.Rupees(60.00m));
        }
    }

    public class Discounts
    {
        private readonly TestShop _shop = new();

        [Fact]
        public void A_percentage_off_a_line_reduces_that_line()
        {
            // SAL-07.
            var sale = _shop.StartSale();
            var line = sale.AddLine(_shop.Item(100.00m), Quantity.Of(1), _shop.StandardVat);

            sale.ApplyLineDiscount(
                line.Id,
                Discount.OfPercentage(0.10m, TestShop.Currency, "Damaged label"),
                TestShop.Now);

            sale.DiscountTotal.ShouldBe(_shop.Rupees(10.00m));
            sale.Total.ShouldBe(_shop.Rupees(90.00m));
        }

        [Fact]
        public void Tax_is_computed_after_the_discount_not_before()
        {
            // Discounting after tax would hand the customer a share of the shop's tax bill.
            var sale = _shop.StartSale();
            var line = sale.AddLine(_shop.Item(118.00m), Quantity.Of(1), _shop.StandardVat);

            sale.ApplyLineDiscount(
                line.Id,
                Discount.OfAmount(_shop.Rupees(18.00m), "Manager goodwill", _shop.ManagerId),
                TestShop.Now);

            sale.Total.ShouldBe(_shop.Rupees(100.00m));
            // 100.00 inclusive of 18% contains 15.25 of tax, not the 18.00 charged before.
            sale.TaxTotal.ShouldBe(_shop.Rupees(15.25m));
        }

        [Fact]
        public void A_fixed_discount_cannot_drive_a_line_negative()
        {
            // Otherwise the discount key becomes a way to take cash out of the drawer.
            var sale = _shop.StartSale();
            var line = sale.AddLine(_shop.Item(50.00m), Quantity.Of(1), _shop.StandardVat);

            sale.ApplyLineDiscount(
                line.Id,
                Discount.OfAmount(_shop.Rupees(500.00m), "Fat finger", _shop.ManagerId),
                TestShop.Now);

            sale.Total.ShouldBe(Money.Zero(TestShop.Currency));
        }

        [Fact]
        public void A_whole_sale_discount_is_apportioned_across_the_lines_without_losing_a_cent()
        {
            // SAL-08. Ten rupees across three equal lines does not divide evenly, and the
            // printed line totals still have to add up to the printed sale total.
            var sale = _shop.StartSale();
            sale.AddLine(_shop.Item(100.00m, sku: "A"), Quantity.Of(1), _shop.StandardVat);
            sale.AddLine(_shop.Item(100.00m, sku: "B"), Quantity.Of(1), _shop.StandardVat);
            sale.AddLine(_shop.Item(100.00m, sku: "C"), Quantity.Of(1), _shop.StandardVat);

            sale.ApplySaleDiscount(
                Discount.OfAmount(_shop.Rupees(10.00m), "Regular customer"),
                TestShop.Now);

            sale.ActiveLines.Select(l => l.ApportionedSaleDiscount.MinorUnits).ShouldBe([334, 333, 333]);
            Money.Sum(sale.ActiveLines.Select(l => l.ApportionedSaleDiscount), TestShop.Currency)
                .ShouldBe(_shop.Rupees(10.00m));
            sale.Total.ShouldBe(_shop.Rupees(290.00m));
            Money.Sum(sale.ActiveLines.Select(l => l.LineTotal), TestShop.Currency).ShouldBe(sale.Total);
        }

        [Fact]
        public void A_whole_sale_discount_follows_the_size_of_each_line()
        {
            var sale = _shop.StartSale();
            sale.AddLine(_shop.Item(700.00m, sku: "BIG"), Quantity.Of(1), _shop.StandardVat);
            sale.AddLine(_shop.Item(300.00m, sku: "SMALL"), Quantity.Of(1), _shop.StandardVat);

            sale.ApplySaleDiscount(
                Discount.OfPercentage(0.10m, TestShop.Currency, "Bulk order"),
                TestShop.Now);

            sale.ActiveLines.First().ApportionedSaleDiscount.ShouldBe(_shop.Rupees(70.00m));
            sale.ActiveLines.Last().ApportionedSaleDiscount.ShouldBe(_shop.Rupees(30.00m));
            sale.Total.ShouldBe(_shop.Rupees(900.00m));
        }

        [Fact]
        public void A_discount_above_the_shops_threshold_needs_a_manager()
        {
            // SAL-09.
            var sale = _shop.StartSale(DiscountPolicy.UpTo(0.10m));
            var line = sale.AddLine(_shop.Item(100.00m), Quantity.Of(1), _shop.StandardVat);

            Should.Throw<DomainException>(() => sale.ApplyLineDiscount(
                line.Id,
                Discount.OfPercentage(0.20m, TestShop.Currency, "Because they asked"),
                TestShop.Now))
                .Message.ShouldContain("manager");
        }

        [Fact]
        public void The_same_discount_is_allowed_once_a_manager_authorises_it()
        {
            // SAL-09 and SEC-05.
            var sale = _shop.StartSale(DiscountPolicy.UpTo(0.10m));
            var line = sale.AddLine(_shop.Item(100.00m), Quantity.Of(1), _shop.StandardVat);

            sale.ApplyLineDiscount(
                line.Id,
                Discount.OfPercentage(0.20m, TestShop.Currency, "Damaged stock", _shop.ManagerId),
                TestShop.Now);

            sale.Total.ShouldBe(_shop.Rupees(80.00m));

            var recorded = sale.DomainEvents.OfType<DiscountApplied>().Single();
            recorded.AuthorisedBy.ShouldBe(_shop.ManagerId);
            recorded.Amount.ShouldBe(_shop.Rupees(20.00m));
        }

        [Fact]
        public void A_discount_within_the_threshold_needs_nobody()
        {
            var sale = _shop.StartSale(DiscountPolicy.UpTo(0.10m));
            var line = sale.AddLine(_shop.Item(100.00m), Quantity.Of(1), _shop.StandardVat);

            sale.ApplyLineDiscount(
                line.Id,
                Discount.OfPercentage(0.05m, TestShop.Currency, "Loyalty"),
                TestShop.Now);

            sale.Total.ShouldBe(_shop.Rupees(95.00m));
        }
    }

    public class Payment
    {
        private readonly TestShop _shop = new();

        [Fact]
        public void Cash_is_rounded_to_the_smallest_coin_and_change_follows_the_rounded_figure()
        {
            // PAY-01 and PAY-02. The shop cannot hand back 40 cents; there is no such coin.
            var sale = _shop.StartSale();
            sale.AddLine(_shop.Item(249.60m), Quantity.Of(1), _shop.StandardVat);

            var change = sale.AddCashPayment(_shop.Rupees(300.00m), TestShop.Now);

            sale.Total.ShouldBe(_shop.Rupees(249.60m));
            sale.CashRoundingAdjustment.ShouldBe(_shop.Rupees(0.40m));
            sale.AmountPayable.ShouldBe(_shop.Rupees(250.00m));
            change.ShouldBe(_shop.Rupees(50.00m));
            sale.BalanceDue.ShouldBe(Money.Zero(TestShop.Currency));
        }

        [Fact]
        public void Rounding_down_is_recorded_as_a_negative_adjustment()
        {
            var sale = _shop.StartSale();
            sale.AddLine(_shop.Item(249.40m), Quantity.Of(1), _shop.StandardVat);

            sale.AddCashPayment(_shop.Rupees(250.00m), TestShop.Now);

            sale.CashRoundingAdjustment.ShouldBe(_shop.Rupees(-0.40m));
            sale.AmountPayable.ShouldBe(_shop.Rupees(249.00m));
            sale.ChangeGiven.ShouldBe(_shop.Rupees(1.00m));
        }

        [Fact]
        public void A_card_payment_settles_the_exact_amount_with_no_rounding()
        {
            // PAY-03. Rounding a card payment would leave the shop permanently out of step
            // with what the acquirer settles.
            var sale = _shop.StartSale();
            sale.AddLine(_shop.Item(249.60m), Quantity.Of(1), _shop.StandardVat);

            sale.AddPayment(PaymentMethod.Card, _shop.Rupees(249.60m), "AUTH-88213", TestShop.Now);

            sale.CashRoundingAdjustment.ShouldBe(Money.Zero(TestShop.Currency));
            sale.AmountPayable.ShouldBe(_shop.Rupees(249.60m));
            sale.BalanceDue.ShouldBe(Money.Zero(TestShop.Currency));
        }

        [Fact]
        public void A_split_tender_rounds_only_the_cash_that_settles_the_balance()
        {
            // PAY-05 with PAY-02.
            var sale = _shop.StartSale();
            sale.AddLine(_shop.Item(249.60m), Quantity.Of(1), _shop.StandardVat);

            sale.AddPayment(PaymentMethod.Card, _shop.Rupees(100.00m), "AUTH-1", TestShop.Now);
            var change = sale.AddCashPayment(_shop.Rupees(200.00m), TestShop.Now);

            // 149.60 outstanding rounds to 150.00, so 50.00 comes back.
            sale.CashRoundingAdjustment.ShouldBe(_shop.Rupees(0.40m));
            change.ShouldBe(_shop.Rupees(50.00m));
            sale.BalanceDue.ShouldBe(Money.Zero(TestShop.Currency));
        }

        [Fact]
        public void Part_paid_cash_does_not_round_because_the_rest_will_settle_exactly()
        {
            var sale = _shop.StartSale();
            sale.AddLine(_shop.Item(249.60m), Quantity.Of(1), _shop.StandardVat);

            sale.AddCashPayment(_shop.Rupees(100.00m), TestShop.Now);

            sale.CashRoundingAdjustment.ShouldBe(Money.Zero(TestShop.Currency));
            sale.BalanceDue.ShouldBe(_shop.Rupees(149.60m));

            sale.AddPayment(PaymentMethod.Card, _shop.Rupees(149.60m), "AUTH-2", TestShop.Now);

            sale.AmountPayable.ShouldBe(_shop.Rupees(249.60m));
            sale.BalanceDue.ShouldBe(Money.Zero(TestShop.Currency));
        }

        [Fact]
        public void A_card_cannot_be_over_tendered()
        {
            var sale = _shop.StartSale();
            sale.AddLine(_shop.Item(100.00m), Quantity.Of(1), _shop.StandardVat);

            Should.Throw<DomainException>(() =>
                sale.AddPayment(PaymentMethod.Card, _shop.Rupees(150.00m), "AUTH-3", TestShop.Now));
        }

        [Fact]
        public void Cash_must_go_through_the_cash_path_so_rounding_cannot_be_skipped()
        {
            var sale = _shop.StartSale();
            sale.AddLine(_shop.Item(100.00m), Quantity.Of(1), _shop.StandardVat);

            Should.Throw<DomainException>(() =>
                sale.AddPayment(PaymentMethod.Cash, _shop.Rupees(100.00m), null, TestShop.Now));
        }

        [Fact]
        public void Only_the_approval_reference_is_kept_never_card_data()
        {
            // PAY-08 and CMP-06. There is nowhere on a payment to put a card number.
            typeof(Sales.Payment).GetProperties()
                .Select(p => p.Name)
                .ShouldNotContain(name =>
                    name.Contains("Pan", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("CardNumber", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("Cvv", StringComparison.OrdinalIgnoreCase));
        }
    }

    public class Completion
    {
        private readonly TestShop _shop = new();

        [Fact]
        public void A_short_payment_blocks_completion()
        {
            // PAY-06.
            var sale = _shop.StartSale();
            sale.AddLine(_shop.Item(100.00m), Quantity.Of(1), _shop.StandardVat);
            sale.AddPayment(PaymentMethod.Card, _shop.Rupees(60.00m), "AUTH-4", TestShop.Now);

            Should.Throw<DomainException>(() => sale.Complete(TestShop.NextReceipt(), TestShop.Now))
                .Message.ShouldContain("still owed");

            sale.Status.ShouldBe(SaleStatus.Open);
        }

        [Fact]
        public void An_empty_basket_cannot_be_completed()
        {
            var sale = _shop.StartSale();

            Should.Throw<DomainException>(() => sale.Complete(TestShop.NextReceipt(), TestShop.Now));
        }

        [Fact]
        public void A_settled_sale_completes_and_announces_what_left_the_shop()
        {
            // INV-03: the stock ledger is written from this event, not by the till directly.
            var sale = _shop.StartSale();
            var product = _shop.Item(118.00m);
            sale.AddLine(product, Quantity.Of(2), _shop.StandardVat);
            sale.AddCashPayment(_shop.Rupees(300.00m), TestShop.Now);

            sale.Complete(TestShop.NextReceipt(7), TestShop.Now);

            sale.Status.ShouldBe(SaleStatus.Completed);
            sale.ReceiptNumber!.Value.ToString().ShouldBe("T1-000007");

            var completed = sale.DomainEvents.OfType<SaleCompleted>().Single();
            completed.Total.ShouldBe(_shop.Rupees(236.00m));
            completed.Items.ShouldHaveSingleItem();
            completed.Items[0].ProductId.ShouldBe(product.Id);
            completed.Items[0].Quantity.ShouldBe(Quantity.Of(2));
        }

        [Fact]
        public void A_completed_sale_can_no_longer_be_changed()
        {
            // CMP-04. Corrections go through a void or a refund, never an edit.
            var sale = CompletedSale();

            Should.Throw<DomainException>(() =>
                sale.AddLine(_shop.Item(10.00m, sku: "LATE"), Quantity.Of(1), _shop.StandardVat));
            Should.Throw<DomainException>(() => sale.Cancel());
        }

        [Fact]
        public void A_receipt_number_is_only_taken_when_the_sale_completes()
        {
            // SYN-03. An abandoned basket must not burn a number, or the sequence gaps and
            // the gap has to be explained to an auditor.
            var sale = _shop.StartSale();
            sale.AddLine(_shop.Item(10.00m), Quantity.Of(1), _shop.StandardVat);

            sale.ReceiptNumber.ShouldBeNull();

            sale.Cancel();

            sale.ReceiptNumber.ShouldBeNull();
        }

        [Fact]
        public void Voiding_a_completed_sale_needs_a_reason_and_puts_the_stock_back()
        {
            // RET-04 and RET-06.
            var sale = CompletedSale();

            sale.Void("Rang up twice", _shop.ManagerId, TestShop.Now);

            sale.Status.ShouldBe(SaleStatus.Voided);
            sale.VoidReason.ShouldBe("Rang up twice");

            var voided = sale.DomainEvents.OfType<SaleVoided>().Single();
            voided.AuthorisedBy.ShouldBe(_shop.ManagerId);
            voided.Items.ShouldNotBeEmpty();
        }

        [Fact]
        public void A_void_without_a_reason_is_refused()
        {
            var sale = CompletedSale();

            Should.Throw<ArgumentException>(() => sale.Void("  ", _shop.ManagerId, TestShop.Now));
        }

        [Fact]
        public void Only_a_completed_sale_can_be_voided()
        {
            var sale = _shop.StartSale();
            sale.AddLine(_shop.Item(10.00m), Quantity.Of(1), _shop.StandardVat);

            Should.Throw<DomainException>(() => sale.Void("Too early", _shop.ManagerId, TestShop.Now));
        }

        private Sale CompletedSale()
        {
            var sale = _shop.StartSale();
            sale.AddLine(_shop.Item(100.00m), Quantity.Of(1), _shop.StandardVat);
            sale.AddCashPayment(_shop.Rupees(100.00m), TestShop.Now);
            sale.Complete(TestShop.NextReceipt(), TestShop.Now);
            return sale;
        }
    }

    public class HoldAndRecall
    {
        private readonly TestShop _shop = new();

        [Fact]
        public void A_basket_can_be_parked_and_picked_up_again()
        {
            // SAL-11. The customer forgot their wallet; the queue keeps moving.
            var sale = _shop.StartSale();
            sale.AddLine(_shop.Item(100.00m), Quantity.Of(1), _shop.StandardVat);

            sale.Hold();
            sale.Status.ShouldBe(SaleStatus.Held);

            sale.Recall();
            sale.Status.ShouldBe(SaleStatus.Open);
            sale.Total.ShouldBe(_shop.Rupees(100.00m));
        }

        [Fact]
        public void A_part_paid_sale_cannot_be_parked()
        {
            // Parking money is how a drawer goes missing.
            var sale = _shop.StartSale();
            sale.AddLine(_shop.Item(100.00m), Quantity.Of(1), _shop.StandardVat);
            sale.AddPayment(PaymentMethod.Card, _shop.Rupees(50.00m), "AUTH-5", TestShop.Now);

            Should.Throw<DomainException>(sale.Hold);
        }
    }

    [Fact]
    public void The_basket_cannot_change_once_money_has_been_taken()
    {
        // Otherwise the rounding already applied to a tender would silently stop matching
        // the total the customer agreed to.
        var sale = _shop.StartSale();
        sale.AddLine(_shop.Item(100.00m), Quantity.Of(1), _shop.StandardVat);
        sale.AddPayment(PaymentMethod.Card, _shop.Rupees(50.00m), "AUTH-6", TestShop.Now);

        Should.Throw<DomainException>(() =>
            sale.AddLine(_shop.Item(10.00m, sku: "EXTRA"), Quantity.Of(1), _shop.StandardVat))
            .Message.ShouldContain("Remove the payments");

        sale.RemoveLastPayment();

        sale.AddLine(_shop.Item(10.00m, sku: "EXTRA"), Quantity.Of(1), _shop.StandardVat);
        sale.Total.ShouldBe(_shop.Rupees(110.00m));
    }

    [Fact]
    public void A_sale_in_another_currency_is_refused()
    {
        var sale = _shop.StartSale();
        sale.AddLine(_shop.Item(100.00m), Quantity.Of(1), _shop.StandardVat);

        Should.Throw<DomainException>(() =>
            sale.AddCashPayment(Money.FromMajor(100m, Currency.USD), TestShop.Now));
    }
}
