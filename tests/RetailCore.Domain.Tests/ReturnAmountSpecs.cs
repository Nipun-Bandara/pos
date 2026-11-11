using RetailCore.Domain.Catalogue;
using RetailCore.Domain.Common;
using RetailCore.Domain.Monetary;
using RetailCore.Domain.Sales;
using RetailCore.Domain.Tax;

namespace RetailCore.Domain.Tests;
public sealed class ReturnAmountSpecs
{
    private static Sale Bill(decimal price=0.05m,UnitOfMeasure unit=UnitOfMeasure.Kilogram,bool cash=false)
    {
        var shop=ShopId.New();var actor=UserId.New();var sale=Sale.Start(shop,TerminalId.New(),ShiftId.New(),actor,Currency.LKR,DateTimeOffset.UtcNow);
        var p=Product.Create(shop,"TEST","Test",CategoryId.New(),unit,Money.FromMajor(price,Currency.LKR),Money.Zero(Currency.LKR),TaxRateId.New());
        sale.AddLine(p,Quantity.One,TaxTreatment.Exempt("NO-TAX"));
        if(cash)sale.AddCashPayment(Money.FromMajor(1,Currency.LKR),DateTimeOffset.UtcNow);
        else sale.AddPayment(PaymentMethod.Card,sale.Total,"AUTH-X",DateTimeOffset.UtcNow);
        sale.Complete(ReceiptNumber.Create("T1",1),DateTimeOffset.UtcNow);return sale;
    }
    [Fact]
    public void Five_cents_in_three_weighted_parts_returns_two_two_one_using_actual_prior_amounts()
    {
        var sale=Bill();var id=sale.Lines[0].Id;long qty=0,paid=0;var results=new List<long>();
        foreach(var q in new long[]{333,333,334})
        {
            var refund=Refund.Against(sale,sale.ShiftId,sale.CashierId,"Return",sale.CashierId,PaymentMethod.Card,DateTimeOffset.UtcNow,
                new Dictionary<SaleLineId,Quantity>{{id,Quantity.FromThousandths(qty)}},alreadyRefundedAmounts:new Dictionary<SaleLineId,long>{{id,paid}});
            refund.AddLine(id,Quantity.FromThousandths(q));results.Add(refund.Total.MinorUnits);qty+=q;paid+=refund.Total.MinorUnits;
        }
        results.ShouldBe(new long[]{2,2,1});paid.ShouldBe(5);
    }
    [Fact]
    public void Prior_quantity_without_actual_amount_and_overpaid_history_fail_closed()
    {
        var sale=Bill();var id=sale.Lines[0].Id;
        Should.Throw<DomainException>(()=>Refund.Against(sale,sale.ShiftId,sale.CashierId,"Return",sale.CashierId,PaymentMethod.Card,DateTimeOffset.UtcNow,
            new Dictionary<SaleLineId,Quantity>{{id,Quantity.Of(.333m)}}));
        Should.Throw<DomainException>(()=>Refund.Against(sale,sale.ShiftId,sale.CashierId,"Return",sale.CashierId,PaymentMethod.Card,DateTimeOffset.UtcNow,
            alreadyRefundedAmounts:new Dictionary<SaleLineId,long>{{id,6}}));
    }
    [Fact]
    public void Refund_entitlement_uses_actual_cash_collected_and_whole_items_cannot_be_fractional()
    {
        var free=Bill(cash:true);ReturnAmounts.Entitlements(free).Values.Single().ShouldBe(0);
        var sale=Bill(100,UnitOfMeasure.Each);var refund=Refund.Against(sale,sale.ShiftId,sale.CashierId,"Return",sale.CashierId,PaymentMethod.Card,DateTimeOffset.UtcNow);
        Should.Throw<DomainException>(()=>refund.AddLine(sale.Lines[0].Id,Quantity.Of(.5m)));
    }
    [Fact]
    public void Exchange_credit_is_explicit_and_removing_it_restores_its_source_state()
    {
        var completed=Bill(100);var sale=Sale.Start(completed.ShopId,completed.TerminalId,completed.ShiftId,completed.CashierId,Currency.LKR,DateTimeOffset.UtcNow);
        var p=Product.Create(sale.ShopId,"NEW","New",CategoryId.New(),UnitOfMeasure.Each,Money.FromMajor(150,Currency.LKR),Money.Zero(Currency.LKR),TaxRateId.New());
        sale.AddLine(p,Quantity.One,TaxTreatment.Exempt("NO-TAX"));
        Should.Throw<DomainException>(()=>sale.AddPayment(PaymentMethod.ExchangeCredit,Money.FromMajor(100,Currency.LKR),null,DateTimeOffset.UtcNow));
        sale.ApplyExchangeCredit(RefundId.New(),10000,0,DateTimeOffset.UtcNow);sale.BalanceDue.MinorUnits.ShouldBe(5000);
        sale.Payments.Single().Method.ShouldBe(PaymentMethod.ExchangeCredit);sale.RemoveLastPayment();sale.ExchangeRefundId.ShouldBeNull();
    }
    [Fact]
    public void Void_after_a_completed_return_is_rejected_before_status_or_events_change()
    {
        var sale=Bill(100);Should.Throw<DomainException>(()=>sale.Void("Void",sale.CashierId,DateTimeOffset.UtcNow,true));sale.Status.ShouldBe(SaleStatus.Completed);
    }
    [Fact]
    public void Invalid_unlinked_exchange_credit_does_not_mutate_refund_settlement_state()
    {
        var sale=Bill(100);var refund=Refund.Against(sale,sale.ShiftId,sale.CashierId,"Return",sale.CashierId,PaymentMethod.Card,DateTimeOffset.UtcNow);
        refund.AddLine(sale.Lines[0].Id,Quantity.One);
        Should.Throw<DomainException>(()=>refund.SetSettlement(0,10000,0,0,null,null));
        refund.CashCreditMinor.ShouldBeNull();refund.ExchangeCreditMinor.ShouldBe(0);
    }

}
