using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailCore.Application.Foundation;
using RetailCore.Application.Checkout;
using RetailCore.Application.Returns;
using RetailCore.Application.Inventory;
using RetailCore.Application.Receipts;
using RetailCore.Domain.Catalogue;
using RetailCore.Domain.Common;
using RetailCore.Domain.Monetary;
using RetailCore.Domain.Sales;
using RetailCore.Domain.Security;
using RetailCore.Infrastructure.Sqlite;
using RetailCore.Infrastructure.Sqlite.Foundation;

namespace RetailCore.Foundation.Tests;
public sealed partial class FoundationSpecs
{
    private static ReturnService Returns(LocalFixture f)=>f.Provider.GetRequiredService<ReturnService>();
    private static async Task<(ReturnDraft Draft,ReturnPreview Preview,ApprovalTicket Ticket)> ReviewReturn(LocalFixture f,ReturnDraft draft)
    {
        var preview=await Returns(f).PreviewAsync(draft);
        var owner=(await f.App.ApproversAsync()).Single(u=>u.Role==Role.Owner);
        var ticket=await f.App.ApproveAsync(owner.Id,Pin,ReturnService.Approval(draft,preview.Hash));return(draft,preview,ticket);
    }
    private static async Task<ReturnDraft> FullReturn(LocalFixture f,Guid original,bool useBasket=false)
    {
        var bill=await Returns(f).LoadAsync(original);
        return new(Guid.NewGuid(),original,bill.Lines.Where(l=>l.Remaining>0).Select(l=>new ReturnSelection(l.Id,(long)(l.Remaining*1000),ReturnDisposition.Saleable)).ToArray(),"Customer return",useBasket);
    }
    private static async Task AddReplacement(LocalFixture f,decimal price)
    {
        var p=(await Catalogue(f).LoadAsync()).Products[0].Product;
        var id=await Catalogue(f).SaveProductAsync(p with{Id=null,Sku="REPLACEMENT",Barcode="00009",NormalPrice=price,Bands=[]});
        await Stock(f).PostAsync(new(Guid.NewGuid(),id,StockEntryKind.Opening,10,50,"Opening",f.Clock.UtcNow));
        var basket=await Billing(f).LoadAsync();await Billing(f).ScanAsync(basket.Basket.Id,basket.Basket.Version,"00009");
    }
    [Theory]
    [InlineData(150,50,0)]
    [InlineData(80,0,20)]
    [InlineData(100,0,0)]
    public async Task Exchange_settles_only_the_difference_and_replacement_returns_preserve_credit_source(int price,int collect,int payout)
    {
        await using var f=new LocalFixture();var owner=await f.SetupAsync();var product=await BillingProduct(f);
        await Billing(f).OpenShiftAsync(Guid.NewGuid(),1000);var original=await CompleteReceiptBill(f);await AddReplacement(f,price);
        var r=await ReviewReturn(f,(await FullReturn(f,original,true)) with{TenderedMinor=10000});
        var result=await Returns(f).CompleteAsync(r.Draft,r.Preview.Hash,r.Ticket);
        result.CollectedMinor.ShouldBe(collect*100);result.CashPayoutMinor.ShouldBe(payout*100);result.CashTenderedMinor.ShouldBe(collect>0?10000:0);
        result.AppliedCreditMinor.ShouldBe(Math.Min(100,price)*100);result.ReplacementNumber.ShouldBe("T1-000003");
        (await Billing(f).LoadAsync()).Shift!.ExpectedCash.ShouldBe(1100+collect-payout);
        (await Stock(f).LoadAsync()).Products.Single(p=>p.Id==product).Balance.ShouldBe(100);
        var replay=await Returns(f).CompleteAsync(r.Draft,r.Preview.Hash,r.Ticket);JsonSerializer.Serialize(replay).ShouldBe(JsonSerializer.Serialize(result));
        await f.RestartAsync();await f.App.StartAsync();await f.App.SignInAsync(owner.Id,Pin);
        (await Returns(f).FindAsync("T1-1")).History.Single().Id.ShouldBe(result.Id);
        await using(var scope=f.Provider.CreateAsyncScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<TillDbContext>();
            var replacement=await db.Sales.IgnoreQueryFilters().Include(s=>s.Payments).SingleAsync(s=>s.Id==new SaleId(result.ReplacementSaleId!.Value));
            replacement.Payments.Single(p=>p.Method==PaymentMethod.ExchangeCredit).Amount.MinorUnits.ShouldBe(Math.Min(100,price)*100);
            replacement.Payments.Where(p=>p.Method==PaymentMethod.Cash).Sum(p=>p.Amount.MinorUnits).ShouldBe(collect*100);
            (await db.Refunds.IgnoreQueryFilters().CountAsync()).ShouldBe(1);(await db.ReceiptCounters.SingleAsync()).LastIssued.ShouldBe(3);
        }
        if(price==150)
        {
            var second=await ReviewReturn(f,await FullReturn(f,result.ReplacementSaleId!.Value));
            (await Returns(f).CompleteAsync(second.Draft,second.Preview.Hash,second.Ticket)).CashPayoutMinor.ShouldBe(15000);
            (await Billing(f).LoadAsync()).Shift!.ExpectedCash.ShouldBe(1000);
        }
    }
    [Fact]
    public async Task Partial_tier_return_uses_original_prices_cost_and_disposition_and_blocks_void_after_return()
    {
        await using var f=new LocalFixture();await f.SetupAsync();var product=await BillingProduct(f);await Billing(f).OpenShiftAsync(Guid.NewGuid(),1000);
        var basket=await Scan(f,await Billing(f).LoadAsync(),3);var original=(await Billing(f).CompleteAsync(Cash(basket,300))).SaleId;
        var input=(await Catalogue(f).LoadAsync()).Products.Single().Product;await Catalogue(f).SaveProductAsync(input with{NormalPrice=999,Cost=888,Bands=[],ReceiptName="වෙනස් නම"});
        var d=await FullReturn(f,original);d=d with{Items=[d.Items[0] with{Thousandths=1000,Disposition=ReturnDisposition.Damaged}]};
        var r=await ReviewReturn(f,d);(await Returns(f).CompleteAsync(r.Draft,r.Preview.Hash,r.Ticket)).CreditMinor.ShouldBe(8500);
        (await Stock(f).LoadAsync()).Products.Single(p=>p.Id==product).Balance.ShouldBe(97);
        (await Should.ThrowAsync<FoundationException>(async()=>await Returns(f).PreviewAsync((await FullReturn(f,original)) with{Void=true}))).Key.ShouldBe("VoidNotEligible");
        var next=await ReviewReturn(f,await FullReturn(f,original));(await Returns(f).CompleteAsync(next.Draft,next.Preview.Hash,next.Ticket)).CreditMinor.ShouldBe(17000);
        (await Billing(f).LoadAsync()).Shift!.ExpectedCash.ShouldBe(1000);
        (await Stock(f).LoadAsync()).Products.Single(p=>p.Id==product).Balance.ShouldBe(99);
        await using var scope=f.Provider.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<TillDbContext>();
        await Should.ThrowAsync<Microsoft.Data.Sqlite.SqliteException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Sales SET Status='Voided' WHERE Id={original}"));
        var refunds=await db.Refunds.IgnoreQueryFilters().Include(r=>r.Lines).ToListAsync();refunds.SelectMany(r=>r.Lines).ShouldAllBe(l=>l.UnitCostMinor==5000);
        (await db.Sales.IgnoreQueryFilters().Include(s=>s.Lines).SingleAsync()).Lines.Single().UnitPrice.MinorUnits.ShouldBe(8500);
    }
    [Fact]
    public async Task Five_cent_weight_return_in_three_visits_totals_five_and_requires_external_card_refund_confirmation()
    {
        await using var f=new LocalFixture();await f.SetupAsync();await BillingProduct(f,.05m,UnitOfMeasure.Kilogram);await Billing(f).OpenShiftAsync(Guid.NewGuid(),0);
        var basket=await Scan(f,await Billing(f).LoadAsync());var request=Cash(basket,.05m) with{Method=PaymentMethod.Card,CardApproved=true};
        var original=(await Billing(f).CompleteAsync(request)).SaleId;long total=0;
        foreach(var quantity in new long[]{333,333,334})
        {
            var draft=await FullReturn(f,original);draft=draft with{Items=[draft.Items[0] with{Thousandths=quantity}]};
            var preview=await Returns(f).PreviewAsync(draft);
            (await Should.ThrowAsync<FoundationException>(()=>Returns(f).CompleteAsync(draft,preview.Hash,null))).Key.ShouldBe("CardRefundApprovalRequired");
            var r=await ReviewReturn(f,draft with{RefundCardApproved=true,CardReference="REF-X"});
            total+=(await Returns(f).CompleteAsync(r.Draft,r.Preview.Hash,r.Ticket)).CardPayoutMinor;
        }
        total.ShouldBe(5);(await Billing(f).LoadAsync()).Shift!.ExpectedCash.ShouldBe(0);
        (await Returns(f).LoadAsync(original)).Lines.Single().Remaining.ShouldBe(0);
    }
    [Fact]
    public async Task Void_is_one_financial_reversal_and_one_stock_reversal_and_cannot_be_returned_again()
    {
        await using var f=new LocalFixture();await f.SetupAsync();var p=await BillingProduct(f);await Billing(f).OpenShiftAsync(Guid.NewGuid(),1000);var original=await CompleteReceiptBill(f);
        var r=await ReviewReturn(f,(await FullReturn(f,original)) with{Void=true});var result=await Returns(f).CompleteAsync(r.Draft,r.Preview.Hash,r.Ticket);
        result.IsVoid.ShouldBeTrue();result.CashPayoutMinor.ShouldBe(10000);(await Billing(f).LoadAsync()).Shift!.ExpectedCash.ShouldBe(1000);
        (await Stock(f).LoadAsync()).Products.Single(p2=>p2.Id==p).Balance.ShouldBe(100);
        await Returns(f).CompleteAsync(r.Draft,r.Preview.Hash,r.Ticket);
        (await Should.ThrowAsync<FoundationException>(async()=>await Returns(f).PreviewAsync(await FullReturn(f,original)))).Key.ShouldBe("ReturnNotEligible");
        await using var scope=f.Provider.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<TillDbContext>();
        (await db.StockMovements.IgnoreQueryFilters().CountAsync(m=>m.SourceType=="SaleVoid")).ShouldBe(1);
        (await db.StockMovements.IgnoreQueryFilters().CountAsync(m=>m.SourceType=="Refund")).ShouldBe(0);
    }
    [Fact]
    public async Task Mid_exchange_sql_failure_rolls_back_both_halves_audit_counter_stock_and_basket_then_retries_once()
    {
        var failure=new FailAfterSave();await using var f=new LocalFixture(failure);await f.SetupAsync();await BillingProduct(f);await Billing(f).OpenShiftAsync(Guid.NewGuid(),1000);
        var original=await CompleteReceiptBill(f);await AddReplacement(f,150);var draft=(await FullReturn(f,original,true)) with{TenderedMinor=10000};
        var r=await ReviewReturn(f,draft);var before=await f.AuditCountAsync();failure.Armed=true;
        await Should.ThrowAsync<InvalidOperationException>(()=>Returns(f).CompleteAsync(r.Draft,r.Preview.Hash,r.Ticket));
        (await f.AuditCountAsync()).ShouldBe(before);(await Billing(f).LoadAsync()).Basket.Items.Count.ShouldBe(1);
        await using(var scope=f.Provider.CreateAsyncScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<TillDbContext>();(await db.Sales.IgnoreQueryFilters().CountAsync()).ShouldBe(1);
            (await db.Refunds.IgnoreQueryFilters().CountAsync()).ShouldBe(0);(await db.Set<ReturnRecord>().CountAsync()).ShouldBe(0);
            (await db.ReceiptCounters.SingleAsync()).LastIssued.ShouldBe(1);(await db.StockMovements.IgnoreQueryFilters().CountAsync(m=>m.SourceType=="Refund")).ShouldBe(0);
        }
        r=await ReviewReturn(f,draft);var result=await Returns(f).CompleteAsync(r.Draft,r.Preview.Hash,r.Ticket);result.Number.ShouldBe("T1-000002");result.ReplacementNumber.ShouldBe("T1-000003");
    }
    [Fact]
    public async Task Approval_is_required_bound_to_the_exact_return_and_locked_sessions_cannot_replay()
    {
        await using var f=new LocalFixture();var owner=await f.SetupAsync();await BillingProduct(f);await Billing(f).OpenShiftAsync(Guid.NewGuid(),1000);var original=await CompleteReceiptBill(f);
        await f.App.CreateUserAsync("Cashier","123789",Role.Cashier,"en");var cashier=(await f.App.UsersAsync()).Single(u=>u.Role==Role.Cashier);
        f.App.SignOut();await f.App.SignInAsync(cashier.Id,"123789");var draft=await FullReturn(f,original);var preview=await Returns(f).PreviewAsync(draft);
        (await Should.ThrowAsync<FoundationException>(()=>Returns(f).CompleteAsync(draft,preview.Hash,null))).Key.ShouldBe("ApprovalRequired");
        var ticket=await f.App.ApproveAsync(owner.Id,Pin,ReturnService.Approval(draft,preview.Hash));
        (await Should.ThrowAsync<FoundationException>(()=>Returns(f).CompleteAsync(draft with{Reason="Different"},preview.Hash,ticket))).Key.ShouldBe("ReturnChanged");
        await Returns(f).CompleteAsync(draft,preview.Hash,ticket);f.App.Lock();
        (await Should.ThrowAsync<FoundationException>(()=>Returns(f).CompleteAsync(draft,preview.Hash,ticket))).Key.ShouldBe("SignInRequired");
    }
    [Fact]
    public async Task Competing_returns_cannot_both_refund_the_same_quantity()
    {
        await using var f=new LocalFixture();await f.SetupAsync();await BillingProduct(f);await Billing(f).OpenShiftAsync(Guid.NewGuid(),1000);var original=await CompleteReceiptBill(f);
        var a=await ReviewReturn(f,await FullReturn(f,original));var b=await ReviewReturn(f,await FullReturn(f,original));
        async Task<bool> Attempt((ReturnDraft Draft,ReturnPreview Preview,ApprovalTicket Ticket) r)
        {try{await Returns(f).CompleteAsync(r.Draft,r.Preview.Hash,r.Ticket);return true;}catch(FoundationException){return false;}}
        (await Task.WhenAll(Attempt(a),Attempt(b))).Count(x=>x).ShouldBe(1);
        await using var scope=f.Provider.CreateAsyncScope();(await scope.ServiceProvider.GetRequiredService<TillDbContext>().Refunds.IgnoreQueryFilters().CountAsync()).ShouldBe(1);
    }
    [Fact]
    public async Task Return_print_retry_after_restart_keeps_one_refund_and_preserves_linked_original_names()
    {
        await using var f=new LocalFixture();var owner=await f.SetupAsync();await BillingProduct(f);await ConfigurePrinter(f);await Billing(f).OpenShiftAsync(Guid.NewGuid(),1000);
        var original=await CompleteReceiptBill(f);var r=await ReviewReturn(f,await FullReturn(f,original));var result=await Returns(f).CompleteAsync(r.Draft,r.Preview.Hash,r.Ticket);
        var bad=new RecordingPrinter(true);var attempt=Guid.NewGuid();(await Receipts(f).PrintReturnAsync(result.Id,attempt,bad)).Status.ShouldBe("PrintFailed");
        await f.RestartAsync();await f.App.StartAsync();await f.App.SignInAsync(owner.Id,Pin);
        var good=new RecordingPrinter();(await Receipts(f).PrintReturnAsync(result.Id,attempt,good)).Status.ShouldBe("PrintAlreadyRequested");good.Jobs.ShouldBeEmpty();
        await Receipts(f).PrintReturnAsync(result.Id,Guid.NewGuid(),good);good.Jobs.Single().Attempt.IsCopy.ShouldBeTrue();good.Jobs.Single().Receipt.OriginalNumber.ShouldBe("T1-000001");
        good.Jobs.Single().Receipt.Returned!.Single().Name.ShouldBe("තේ පැකට්");
        await using var scope=f.Provider.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<TillDbContext>();(await db.Refunds.IgnoreQueryFilters().CountAsync()).ShouldBe(1);
        await Should.ThrowAsync<Microsoft.Data.Sqlite.SqliteException>(()=>db.Database.ExecuteSqlRawAsync("UPDATE ReturnDocuments SET ResultJson='changed'"));
        await Should.ThrowAsync<Microsoft.Data.Sqlite.SqliteException>(()=>db.Database.ExecuteSqlRawAsync("DELETE FROM RefundLines"));
    }
    [Fact]
    public async Task Historical_split_cash_card_and_exchange_credit_keep_the_original_tender_allocation()
    {
        await using var f=new LocalFixture();await f.SetupAsync();await BillingProduct(f);await Billing(f).OpenShiftAsync(Guid.NewGuid(),1000);
        var original=await f.Transactions.WriteAsync(async(store,ct)=>
        {
            var shop=(await store.ShopsAsync(ct)).Single();var terminal=(await store.TerminalsAsync(shop.Id,ct)).Single();
            var shift=(await store.OpenShiftAsync(shop.Id,terminal.Id,ct))!;var actor=(await store.UsersAsync(shop.Id,ct)).Single();
            var sale=Sale.Start(shop.Id,terminal.Id,shift.Id,actor.Id,shop.Currency,f.Clock.UtcNow);
            sale.AddPricedLine((await store.ProductsAsync(shop.Id,ct)).Single(),Quantity.One,(await store.TaxRatesAsync(shop.Id,ct)).Single().ToTreatment());
            sale.AddPayment(PaymentMethod.Card,Money.FromMajor(60,shop.Currency),"AUTH-X",f.Clock.UtcNow);sale.AddCashPayment(Money.FromMajor(40,shop.Currency),f.Clock.UtcNow);
            sale.Complete(await store.NextReceiptAsync(shop.Id,terminal.Id,ct),f.Clock.UtcNow);store.Add(sale);store.AddReceipt(shop.Id,ReceiptDocument.Capture(sale,shop,actor.DisplayName));return sale.Id.Value;
        });
        await AddReplacement(f,80);var r=await ReviewReturn(f,(await FullReturn(f,original,true)) with{RefundCardApproved=true});
        var result=await Returns(f).CompleteAsync(r.Draft,r.Preview.Hash,r.Ticket);result.CashPayoutMinor.ShouldBe(0);result.CardPayoutMinor.ShouldBe(2000);
        (await Billing(f).LoadAsync()).Shift!.ExpectedCash.ShouldBe(1040);
        var next=await ReviewReturn(f,(await FullReturn(f,result.ReplacementSaleId!.Value)) with{RefundCardApproved=true});
        var paid=await Returns(f).CompleteAsync(next.Draft,next.Preview.Hash,next.Ticket);paid.CashPayoutMinor.ShouldBe(4000);paid.CardPayoutMinor.ShouldBe(4000);
        (await Billing(f).LoadAsync()).Shift!.ExpectedCash.ShouldBe(1000);
    }
    [Fact]
    public async Task Replacement_shortage_requires_explicit_reasoned_override_and_stale_prices_invalidate_approval()
    {
        await using var f=new LocalFixture();await f.SetupAsync();await BillingProduct(f);await Billing(f).OpenShiftAsync(Guid.NewGuid(),1000);
        var original=await CompleteReceiptBill(f);await AddReplacement(f,150);var basket=await Billing(f).LoadAsync();
        var replacement=basket.Basket.Items.Single().ProductId;await Billing(f).EditAsync(new(basket.Basket.Id,basket.Basket.Version,replacement,11));
        var draft=(await FullReturn(f,original,true)) with{TenderedMinor=200000};var preview=await Returns(f).PreviewAsync(draft);preview.NegativeStock.ShouldBeTrue();
        (await Should.ThrowAsync<FoundationException>(()=>Returns(f).CompleteAsync(draft,preview.Hash,null))).Key.ShouldBe("InsufficientStock");
        var r=await ReviewReturn(f,draft with{AllowNegative=true,NegativeReason="Owner accepts shortage"});
        var product=(await Catalogue(f).LoadAsync()).Products.Single(p=>p.Sku=="REPLACEMENT").Product;
        await Catalogue(f).SaveProductAsync(product with{NormalPrice=160});
        (await Should.ThrowAsync<FoundationException>(()=>Returns(f).CompleteAsync(r.Draft,r.Preview.Hash,r.Ticket))).Key.ShouldBe("ReturnChanged");
        r=await ReviewReturn(f,r.Draft);await Returns(f).CompleteAsync(r.Draft,r.Preview.Hash,r.Ticket);
        (await Stock(f).LoadAsync()).Products.Single(p=>p.Id==replacement).Balance.ShouldBe(-1);
        await using var scope=f.Provider.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<TillDbContext>();
        (await db.StockMovements.IgnoreQueryFilters().Where(m=>m.SourceType=="Sale" && m.ProductId==replacement).SingleAsync()).ApprovedBy.ShouldNotBeNull();
    }

    [Fact]
    public async Task Additional_card_collection_requires_external_approval_and_rejects_card_credentials()
    {
        await using var f=new LocalFixture();await f.SetupAsync();await BillingProduct(f);await Billing(f).OpenShiftAsync(Guid.NewGuid(),1000);
        var original=await CompleteReceiptBill(f);await AddReplacement(f,150);
        var draft=(await FullReturn(f,original,true)) with{CollectionMethod=PaymentMethod.Card};var preview=await Returns(f).PreviewAsync(draft);
        (await Should.ThrowAsync<FoundationException>(()=>Returns(f).CompleteAsync(draft,preview.Hash,null))).Key.ShouldBe("CardApprovalRequired");
        (await Should.ThrowAsync<FoundationException>(()=>Returns(f).PreviewAsync(draft with{CollectionCardApproved=true,CardReference="4111111111111111"}))).Key.ShouldBe("InvalidReturn");
        var r=await ReviewReturn(f,draft with{CollectionCardApproved=true,CardReference="AUTH-X"});
        var result=await Returns(f).CompleteAsync(r.Draft,r.Preview.Hash,r.Ticket);result.CollectedMinor.ShouldBe(5000);
        (await Billing(f).LoadAsync()).Shift!.ExpectedCash.ShouldBe(1100);
        await using var scope=f.Provider.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<TillDbContext>();
        var replacement=await db.Sales.IgnoreQueryFilters().Include(s=>s.Payments).SingleAsync(s=>s.Id==new SaleId(result.ReplacementSaleId!.Value));
        replacement.Payments.Single(p=>p.Method==PaymentMethod.Card).Amount.MinorUnits.ShouldBe(5000);
        replacement.Payments.Any(p=>p.Method==PaymentMethod.Cash).ShouldBeFalse();
    }

}
