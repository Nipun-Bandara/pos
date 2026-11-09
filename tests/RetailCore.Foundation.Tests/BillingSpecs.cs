using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using RetailCore.Application.Checkout;
using RetailCore.Application.Foundation;
using RetailCore.Application.Inventory;
using RetailCore.Domain.Catalogue;
using RetailCore.Domain.Common;
using RetailCore.Domain.Monetary;
using RetailCore.Domain.Sales;
using RetailCore.Domain.Security;
using RetailCore.Infrastructure.Persistence;
using RetailCore.Infrastructure.Sqlite;

namespace RetailCore.Foundation.Tests;

public sealed partial class FoundationSpecs
{
    private static BillingService Billing(LocalFixture f) => f.Provider.GetRequiredService<BillingService>();
    private static async Task<ProductId> BillingProduct(LocalFixture f, decimal normal = 100, UnitOfMeasure unit = UnitOfMeasure.Each)
    {
        var category = await Catalogue(f).SaveCategoryAsync(null, "පාන", "Drinks", true);
        var p = await Catalogue(f).SaveProductAsync(Tea(category) with { NormalPrice = normal, Unit = unit,
            Bands = normal == 100 ? Tea(category).Bands : [] });
        await Stock(f).PostAsync(new StockEntryInput(Guid.NewGuid(), p, StockEntryKind.Opening, 100, 50, "Opening", f.Clock.UtcNow));
        return p;
    }
    private static CompletionRequest Cash(BillingState state, decimal tendered) => new(state.Basket.Id, state.Basket.Version,
        state.Quote.Hash, PaymentMethod.Cash, Money.FromMajor(tendered, Currency.FromCode(state.Quote.Currency)).MinorUnits);
    private static Task<BillingState> Scan(LocalFixture f, BillingState state, decimal quantity = 1) =>
        Billing(f).ScanAsync(state.Basket.Id, state.Basket.Version, "001234567890", quantity);
    private static async Task<ApprovalTicket> CheckoutApproval(LocalFixture f, CompletionRequest request)
    {
        var owner = (await f.App.ApproversAsync()).Single();
        return await f.App.ApproveAsync(owner.Id, Pin, BillingService.CompletionApproval(request));
    }

    [Fact]
    public async Task Repeated_scans_aggregate_and_reprice_1_to_5_then_2_and_draft_survives_restart()
    {
        await using var f = new LocalFixture(); var owner = await f.SetupAsync();
        var p = await BillingProduct(f);
        var state = await Billing(f).LoadAsync();
        foreach (var total in new long[] { 10000, 18000, 25500, 32000, 40000 })
        {
            state = await Scan(f, state);
            state.Quote.TotalMinor.ShouldBe(total); state.Quote.Lines.Count.ShouldBe(1);
        }
        state = await Billing(f).EditAsync(new(state.Basket.Id, state.Basket.Version, p, 2));
        state.Quote.TotalMinor.ShouldBe(18000); state.Quote.Lines.Single().TierBand.ShouldBe(2);
        await f.RestartAsync(); await f.App.StartAsync(); await f.App.SignInAsync(owner.Id, Pin);
        var restored = await Billing(f).LoadAsync();
        restored.Basket.Id.ShouldBe(state.Basket.Id); restored.Quote.TotalMinor.ShouldBe(18000);
        var category = await Catalogue(f).SaveCategoryAsync(null, "එළවළු", "Vegetables", true);
        var weighed = await Catalogue(f).SaveProductAsync(Tea(category) with { Sku = "WEIGHT", Barcode = "00009", Unit = UnitOfMeasure.Kilogram, NormalPrice = 150, Bands = [] });
        await Stock(f).PostAsync(new(Guid.NewGuid(), weighed, StockEntryKind.Opening, 10, 50, "Opening", f.Clock.UtcNow));
        restored = await Billing(f).ScanAsync(restored.Basket.Id, restored.Basket.Version, "00009", 1.250m);
        restored.Quote.Lines.Single(l => l.ProductId == weighed).Total.ShouldBe(187.50m);
        (await Should.ThrowAsync<FoundationException>(() => Billing(f).ScanAsync(restored.Basket.Id, restored.Basket.Version, "00009", 1.2501m))).Key.ShouldBe("InvalidBillingQuantity");
        restored = await Billing(f).EditAsync(new(restored.Basket.Id, restored.Basket.Version, weighed, 0, Remove: true));
        restored.Quote.TotalMinor.ShouldBe(18000);
        (await Should.ThrowAsync<FoundationException>(() => Billing(f).EditAsync(new(restored.Basket.Id, restored.Basket.Version, p, .5m))))
            .Key.ShouldBe("InvalidBillingQuantity");
    }

    [Fact]
    public async Task Cash_150_tender_200_gives_50_change_and_drawer_rises_by_150_retry_after_restart_is_one_sale()
    {
        await using var f = new LocalFixture(); var owner = await f.SetupAsync(); var p = await BillingProduct(f, 150);
        var open = Guid.NewGuid(); var shift = await Billing(f).OpenShiftAsync(open, 1000);
        (await Billing(f).OpenShiftAsync(open, 1000)).ShouldBe(shift);
        (await Should.ThrowAsync<FoundationException>(() => Billing(f).OpenShiftAsync(Guid.NewGuid(), 1000))).Key.ShouldBe("ShiftAlreadyOpen");
        var state = await Scan(f, await Billing(f).LoadAsync()); var request = Cash(state, 200);
        var concurrent = await Task.WhenAll(Billing(f).CompleteAsync(request), Billing(f).CompleteAsync(request));
        concurrent[0].ShouldBe(concurrent[1]); var result = concurrent[0];
        result.TotalMinor.ShouldBe(15000); result.PaidMinor.ShouldBe(15000); result.TenderedMinor.ShouldBe(20000); result.ChangeMinor.ShouldBe(5000);
        var saved = await Billing(f).LoadAsync(); saved.Basket.Items.ShouldBeEmpty(); saved.Shift!.ExpectedCash.ShouldBe(1150);
        await f.RestartAsync(); await f.App.StartAsync(); await f.App.SignInAsync(owner.Id, Pin);
        saved = await Billing(f).LoadAsync(); saved.Basket.LastResult.ShouldBe(result);
        (await Billing(f).CompleteAsync(saved.Basket.LastRequest!)).ShouldBe(result);
        var product = (await Catalogue(f).LoadAsync()).Products.Single().Product;
        await Catalogue(f).SaveProductAsync(product with { NormalPrice = 999, EnglishName = "New tea", ReceiptName = "නව තේ" });
        (await Billing(f).CompleteAsync(request)).ShouldBe(result);
        await using var scope = f.Provider.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<TillDbContext>();
        (await db.Sales.IgnoreQueryFilters().CountAsync()).ShouldBe(1);
        var sale = await db.Sales.IgnoreQueryFilters().Include(s => s.Lines).Include(s => s.Payments).SingleAsync();
        sale.Lines.Single().ReceiptNameSnapshot.ShouldBe("තේ පැකට්"); sale.Lines.Single().UnitCost.ToDecimal().ShouldBe(50);
        sale.Payments.Single().Amount.ToDecimal().ShouldBe(150); sale.Payments.Single().Tendered.ToDecimal().ShouldBe(200);
        (await db.StockMovements.IgnoreQueryFilters().CountAsync(m => m.SourceType == "Sale")).ShouldBe(1);
        (await db.ReceiptCounters.SingleAsync()).LastIssued.ShouldBe(1);
        (await db.AuditEntries.IgnoreQueryFilters().CountAsync(a => a.Action == AuditAction.SaleCompleted)).ShouldBe(1);
        (await Stock(f).LoadAsync()).Products.Single(r => r.Id == p).Balance.ShouldBe(99);
    }

    [Fact]
    public async Task Configured_rounding_is_cash_only_and_exact_card_recording_does_not_inflate_drawer()
    {
        await using var f = new LocalFixture(); var owner = await f.SetupAsync(); await BillingProduct(f, 150.26m);
        var profile = Profile with { CashIncrementMinor = 50 };
        var ticket = await f.App.ApproveAsync(owner.Id, Pin, FoundationService.SettingsApproval(profile)); await f.App.SaveProfileAsync(profile, ticket);
        await Billing(f).OpenShiftAsync(Guid.NewGuid(), 0);
        var state = await Scan(f, await Billing(f).LoadAsync());
        state.Quote.CashDueMinor.ShouldBe(15050); state.Quote.CashRoundingMinor.ShouldBe(24);
        var cash = await Billing(f).CompleteAsync(Cash(state, 200)); cash.ChangeMinor.ShouldBe(4950);
        state = await Scan(f, await Billing(f).LoadAsync());
        var card = Cash(state, 150.26m) with { Method = PaymentMethod.Card, CardReference = "AUTH-4471" };
        (await Should.ThrowAsync<FoundationException>(() => Billing(f).CompleteAsync(card))).Key.ShouldBe("CardApprovalRequired");
        card = card with { CardApproved = true };
        var result = await Billing(f).CompleteAsync(card); result.PaidMinor.ShouldBe(15026); result.RoundingMinor.ShouldBe(0); result.ChangeMinor.ShouldBe(0);
        (await Billing(f).LoadAsync()).Shift!.ExpectedCash.ShouldBe(150.50m);
    }

    [Fact]
    public async Task Settlement_rejects_closed_shift_short_tender_stale_quote_and_stale_cart_without_writes()
    {
        await using var f = new LocalFixture(); await f.SetupAsync(); var p = await BillingProduct(f, 150);
        var state = await Scan(f, await Billing(f).LoadAsync());
        (await Should.ThrowAsync<FoundationException>(() => Billing(f).CompleteAsync(Cash(state, 200)))).Key.ShouldBe("OpenShiftRequired");
        await Billing(f).OpenShiftAsync(Guid.NewGuid(), 0); state = await Billing(f).LoadAsync();
        (await Should.ThrowAsync<FoundationException>(() => Billing(f).CompleteAsync(Cash(state, 149)))).Key.ShouldBe("InsufficientTender");
        var old = state; state = await Scan(f, state);
        (await Should.ThrowAsync<FoundationException>(() => Billing(f).CompleteAsync(Cash(old, 400)))).Key.ShouldBe("StaleBasket");
        var input = (await Catalogue(f).LoadAsync()).Products.Single().Product;
        await Catalogue(f).SaveProductAsync(input with { NormalPrice = 155 });
        (await Should.ThrowAsync<FoundationException>(() => Billing(f).CompleteAsync(Cash(state, 400)))).Key.ShouldBe("CheckoutChanged");
        state = await Billing(f).LoadAsync();
        await Catalogue(f).SaveProductAsync(input with { Active = false });
        state = await Billing(f).LoadAsync(); state.Quote.ErrorKey.ShouldBe("BillingProductUnavailable");
        state = await Billing(f).EditAsync(new(state.Basket.Id, state.Basket.Version, p, 0, Remove: true)); state.Basket.Items.ShouldBeEmpty();
        await using var scope = f.Provider.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<TillDbContext>();
        (await db.Sales.IgnoreQueryFilters().CountAsync()).ShouldBe(0); (await db.ReceiptCounters.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Proposed_discount_requires_exact_owner_grant_and_snapshots_final_price_without_stacking_tiers()
    {
        await using var f = new LocalFixture(); await f.SetupAsync(); var p = await BillingProduct(f);
        await f.App.CreateUserAsync("Cashier", "123789", Role.Cashier, "si");
        var cashier = (await f.App.UsersAsync()).Single(u => u.Role == Role.Cashier); await f.App.SignInAsync(cashier.Id, "123789");
        await Billing(f).OpenShiftAsync(Guid.NewGuid(), 0);
        var state = await Scan(f, await Billing(f).LoadAsync(), 3);
        state = await Billing(f).EditAsync(new(state.Basket.Id, state.Basket.Version, p, 3, RequestedUnitPrice: 80, Reason: "Owner offer"));
        state.Quote.TotalMinor.ShouldBe(24000); state.Quote.Lines.Single().TierUnitPrice.ShouldBe(85);
        var request = Cash(state, 300);
        (await Should.ThrowAsync<FoundationException>(() => Billing(f).CompleteAsync(request))).Key.ShouldBe("ApprovalRequired");
        var wrong = await CheckoutApproval(f, request with { TenderedMinor = 40000 });
        (await Should.ThrowAsync<FoundationException>(() => Billing(f).CompleteAsync(request, wrong))).Key.ShouldBe("ApprovalRequired");
        var approved = await CheckoutApproval(f, request); await Billing(f).CompleteAsync(request, approved);
        await using var scope = f.Provider.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<TillDbContext>();
        var line = await db.Set<SaleLine>().IgnoreQueryFilters().SingleAsync();
        line.UnitPrice.ToDecimal().ShouldBe(80); line.ManualPriceReason.ShouldBe("Owner offer");
        line.ManualPriceApprovedBy.ShouldNotBeNull(); line.AppliedPriceBand.ShouldBeNull(); line.QualifiedPriceBand.ShouldBe(2);
        line.QualifiedTierUnitPriceMinor.ShouldBe(8500); line.NormalUnitPriceMinor.ShouldBe(10000);
        (await db.AuditEntries.IgnoreQueryFilters().CountAsync(a => a.Action == AuditAction.PriceOverridden)).ShouldBe(1);
    }

    [Fact]
    public async Task Stock_shortage_requires_opt_in_reason_and_owner_approval_bound_to_current_stock()
    {
        await using var f = new LocalFixture(); await f.SetupAsync(); var p = await BillingProduct(f, 150);
        await Billing(f).OpenShiftAsync(Guid.NewGuid(), 0);
        var state = await Scan(f, await Billing(f).LoadAsync(), 101); var request = Cash(state, 16000);
        state.Quote.NeedsStockApproval.ShouldBeTrue();
        (await Should.ThrowAsync<FoundationException>(() => Billing(f).CompleteAsync(request))).Key.ShouldBe("InsufficientStock");
        request = request with { AllowNegative = true, NegativeReason = "Count mismatch" };
        (await Should.ThrowAsync<FoundationException>(() => Billing(f).CompleteAsync(request))).Key.ShouldBe("ApprovalRequired");
        var ticket = await CheckoutApproval(f, request);
        await Stock(f).PostAsync(new(Guid.NewGuid(), p, StockEntryKind.Receiving, 1, 50, "Receipt after preview", f.Clock.UtcNow));
        (await Should.ThrowAsync<FoundationException>(() => Billing(f).CompleteAsync(request, ticket))).Key.ShouldBe("CheckoutChanged");
        state = await Scan(f, await Billing(f).LoadAsync());
        request = Cash(state, 16000) with { AllowNegative = true, NegativeReason = "Count mismatch" };
        await Billing(f).CompleteAsync(request, await CheckoutApproval(f, request));
        (await Stock(f).LoadAsync()).Products.Single().Balance.ShouldBe(-1);
        await using var scope = f.Provider.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<TillDbContext>().AuditEntries.IgnoreQueryFilters().CountAsync(a => a.Action == AuditAction.NegativeStockApproved)).ShouldBe(1);
    }

    [Fact]
    public async Task Failure_after_sql_rolls_back_sale_payment_stock_audit_counter_cart_and_command_then_retry_uses_first_receipt()
    {
        var failure = new FailAfterSave(); await using var f = new LocalFixture(failure); await f.SetupAsync(); await BillingProduct(f, 150);
        await Billing(f).OpenShiftAsync(Guid.NewGuid(), 0); var state = await Scan(f, await Billing(f).LoadAsync());
        var request = Cash(state, 200); var before = await f.AuditCountAsync(); failure.Armed = true;
        await Should.ThrowAsync<InvalidOperationException>(() => Billing(f).CompleteAsync(request));
        await using (var scope = f.Provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TillDbContext>();
            (await db.Sales.IgnoreQueryFilters().CountAsync()).ShouldBe(0);
            (await db.Set<Payment>().IgnoreQueryFilters().CountAsync()).ShouldBe(0);
            (await db.StockMovements.IgnoreQueryFilters().CountAsync(m => m.SourceType == "Sale")).ShouldBe(0);
            (await db.ReceiptCounters.CountAsync()).ShouldBe(0);
            (await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM LocalCommands WHERE Kind='CompleteSale'").SingleAsync()).ShouldBe(0);
        }
        (await f.AuditCountAsync()).ShouldBe(before);
        var restored = await Billing(f).LoadAsync(); restored.Basket.Id.ShouldBe(state.Basket.Id); restored.Basket.Items.Count.ShouldBe(1);
        var result = await Billing(f).CompleteAsync(request); result.Receipt.ShouldBe("T1-000001");
        (await Billing(f).CompleteAsync(request)).ShouldBe(result);
    }

    [Theory]
    [InlineData("4111111111111111")]
    [InlineData("CVV123")]
    [InlineData("PIN-1234")]
    public async Task Card_credentials_are_rejected_before_any_payment_request_or_audit_is_persisted(string reference)
    {
        await using var f = new LocalFixture(); await f.SetupAsync(); await BillingProduct(f, 150); await Billing(f).OpenShiftAsync(Guid.NewGuid(), 0);
        var state = await Scan(f, await Billing(f).LoadAsync()); var before = await f.AuditCountAsync();
        var request = Cash(state, 150) with { Method = PaymentMethod.Card, CardApproved = true, CardReference = reference };
        (await Should.ThrowAsync<FoundationException>(() => Billing(f).CompleteAsync(request))).Key.ShouldBe("InvalidCardReference");
        (await f.AuditCountAsync()).ShouldBe(before);
        await using var scope = f.Provider.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<TillDbContext>();
        (await db.Set<Payment>().IgnoreQueryFilters().CountAsync()).ShouldBe(0);
        (await db.Database.SqlQueryRaw<string>("SELECT DocumentJson AS Value FROM BillingBaskets").SingleAsync()).ShouldNotContain(reference);
    }

    [Fact]
    public async Task Mixed_pack_and_loose_billing_posts_one_shared_balance_with_historical_snapshots()
    {
        await using var f = new LocalFixture(); await f.SetupAsync(); var (root, pack) = await StockProducts(f);
        await Stock(f).PostAsync(Entry(f, pack)); await Billing(f).OpenShiftAsync(Guid.NewGuid(), 0);
        var state = await Billing(f).LoadAsync();
        state = await Billing(f).ScanAsync(state.Basket.Id, state.Basket.Version, "00001", 1.250m);
        state = await Billing(f).ScanAsync(state.Basket.Id, state.Basket.Version, "00002");
        state.Quote.Stock.Single().Remaining.ShouldBe(23.750m);
        await Billing(f).CompleteAsync(Cash(state, 4000));
        (await Stock(f).LoadAsync()).Products.ShouldAllBe(p => p.Balance == 23.750m);
        await using var scope = f.Provider.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<TillDbContext>();
        var line = await db.Set<SaleLine>().IgnoreQueryFilters().SingleAsync(l => l.ProductId == pack);
        line.StockProductId.ShouldBe(root); line.StockFactorThousandths.ShouldBe(25000);
        line.ReceiptNameSnapshot.ShouldBe("සහල් බෑගය"); line.UnitCost.ToDecimal().ShouldBe(2500);
    }

    [Fact]
    public async Task Locked_sessions_and_other_cashiers_cannot_edit_or_complete_another_users_cart()
    {
        await using var f = new LocalFixture(); var owner = await f.SetupAsync(); var p = await BillingProduct(f, 150);
        var state = await Scan(f, await Billing(f).LoadAsync());
        await f.App.CreateUserAsync("Cashier", "123789", Role.Cashier, "en"); var cashier = (await f.App.UsersAsync()).Single(u => u.Role == Role.Cashier);
        await f.App.SignInAsync(cashier.Id, "123789");
        (await Billing(f).LoadAsync()).Basket.Items.ShouldBeEmpty();
        (await Should.ThrowAsync<FoundationException>(() => Billing(f).EditAsync(new(state.Basket.Id, state.Basket.Version, p, 2)))).Key.ShouldBe("StaleBasket");
        f.App.Lock();
        (await Should.ThrowAsync<FoundationException>(() => Billing(f).CompleteAsync(Cash(state, 200)))).Key.ShouldBe("SignInRequired");
        await f.App.SignInAsync(owner.Id, Pin); (await Billing(f).LoadAsync()).Basket.Items.Count.ShouldBe(1);
    }
    [Fact]
    public async Task A_closed_shift_blocks_settlement_and_reopening_requires_a_fresh_quote()
    {
        await using var f = new LocalFixture(); var owner = await f.SetupAsync(); await BillingProduct(f, 150);
        await Billing(f).OpenShiftAsync(Guid.NewGuid(), 0);
        var state = await Scan(f, await Billing(f).LoadAsync()); var request = Cash(state, 200);
        await using (var scope = f.Provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TillDbContext>();
            var shift = await db.Shifts.IgnoreQueryFilters().SingleAsync();
            shift.Close(await new ShiftRepository(db).SalesSummaryAsync(shift.Id), Money.Zero(Currency.FromCode("LKR")), owner.Id, f.Clock.UtcNow);
            await db.SaveChangesAsync();
        }
        (await Should.ThrowAsync<FoundationException>(() => Billing(f).CompleteAsync(request))).Key.ShouldBe("OpenShiftRequired");
        await Billing(f).OpenShiftAsync(Guid.NewGuid(), 0);
        (await Should.ThrowAsync<FoundationException>(() => Billing(f).CompleteAsync(request))).Key.ShouldBe("CheckoutChanged");
        state = await Billing(f).LoadAsync(); await Billing(f).CompleteAsync(Cash(state, 200));
    }

    [Fact]
    public async Task Expired_or_revoked_owner_approval_never_authorizes_a_discount_write()
    {
        await using var f = new LocalFixture(); var owner = await f.SetupAsync(); var p = await BillingProduct(f);
        await Billing(f).OpenShiftAsync(Guid.NewGuid(), 0);
        var state = await Scan(f, await Billing(f).LoadAsync());
        state = await Billing(f).EditAsync(new(state.Basket.Id, state.Basket.Version, p, 1, RequestedUnitPrice: 90, Reason: "Offer"));
        var request = Cash(state, 100); var ticket = await CheckoutApproval(f, request);
        f.Clock.UtcNow += TimeSpan.FromMinutes(3);
        (await Should.ThrowAsync<FoundationException>(() => Billing(f).CompleteAsync(request, ticket))).Key.ShouldBe("ApprovalRequired");
        ticket = await CheckoutApproval(f, request);
        await f.App.CreateUserAsync("Second owner", "832194", Role.Owner, "en");
        var other = (await f.App.UsersAsync()).Single(u => u.Id != owner.Id);
        await using var provider = f.AnotherSession(); var app = provider.GetRequiredService<FoundationService>();
        await app.StartAsync(); await app.SignInAsync(other.Id, "832194");
        await app.UpdateUserAsync(owner.Id, Role.Cashier, true, "998431", false);
        (await Should.ThrowAsync<FoundationException>(() => Billing(f).CompleteAsync(request, ticket))).Key.ShouldBe("SignInRequired");
        await using var scope = f.Provider.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<TillDbContext>().Sales.IgnoreQueryFilters().CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Rounded_zero_cash_checkout_still_has_one_receipt_one_stock_effect_and_an_explicit_rounding_record()
    {
        await using var f = new LocalFixture(); await f.SetupAsync(); await BillingProduct(f, .01m);
        await Billing(f).OpenShiftAsync(Guid.NewGuid(), 0);
        var state = await Scan(f, await Billing(f).LoadAsync()); var request = Cash(state, 0);
        state.Quote.TotalMinor.ShouldBe(1); state.Quote.CashRoundingMinor.ShouldBe(-1); state.Quote.CashDueMinor.ShouldBe(0);
        var result = await Billing(f).CompleteAsync(request); result.PaidMinor.ShouldBe(0); result.RoundingMinor.ShouldBe(-1);
        (await Billing(f).CompleteAsync(request)).ShouldBe(result);
        (await Billing(f).LoadAsync()).Shift!.ExpectedCash.ShouldBe(0);
        (await Stock(f).LoadAsync()).Products.Single().Balance.ShouldBe(99);
    }

    [Fact]
    public async Task Checkout_migration_preserves_a_completed_tier_bill_payment_and_historical_names()
    {
        await using var f = new LocalFixture();
        var sale = Guid.NewGuid(); var shop = Guid.NewGuid(); var terminal = Guid.NewGuid(); var shift = Guid.NewGuid();
        var actor = Guid.NewGuid(); var product = Guid.NewGuid(); var line = Guid.NewGuid(); var payment = Guid.NewGuid();
        await using (var scope = f.Provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TillDbContext>();
            await db.GetService<IMigrator>().MigrateAsync("20261109160236_StockManagement");
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Sales (Id,ShopId,TerminalId,ShiftId,CashierId,CurrencyCode,Status,ReceiptNumber,StartedAt,CompletedAt,MaxUnauthorisedDiscountRate,CashRoundingCurrency,CashRoundingMinor,DiscountCurrency,DiscountMinor,GrossCurrency,GrossMinor,NetCurrency,NetMinor,TaxCurrency,TaxMinor,TotalCurrency,TotalMinor) VALUES ({sale},{shop},{terminal},{shift},{actor},'LKR','Completed','T1-000007',{f.Clock.UtcNow},{f.Clock.UtcNow},'0.1','LKR',0,'LKR',0,'LKR',18000,'LKR',18000,'LKR',0,'LKR',18000)");
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO SaleLines (Id,SaleId,ShopId,LineNumber,ProductId,Sku,Description,Unit,IsVoided,ApportionedDiscountCurrency,ApportionedDiscountMinor,QuantityThousandths,TaxCategory,TaxCode,TaxRate,UnitCostCurrency,UnitCostMinor,UnitPriceCurrency,UnitPriceMinor,EnglishNameSnapshot,SinhalaNameSnapshot,ReceiptNameSnapshot,AppliedPriceBand,AppliedMinimumThousandths) VALUES ({line},{sale},{shop},1,{product},'OLD','Historical tea','Each',0,'LKR',0,2000,'Exempt','NO-TAX','0','LKR',5000,'LKR',9000,'Historical tea',{"පැරණි තේ"},{"පැරණි තේ"},1,2000)");
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Payments (Id,SaleId,ShopId,Method,TakenAt,AmountCurrency,AmountMinor,TenderedCurrency,TenderedMinor) VALUES ({payment},{sale},{shop},'Cash',{f.Clock.UtcNow},'LKR',18000,'LKR',20000)");
        }
        await f.Transactions.InitializeAsync();
        await using var read = f.Provider.CreateAsyncScope();
        var saved = await read.ServiceProvider.GetRequiredService<TillDbContext>().Sales.IgnoreQueryFilters()
            .Include(s => s.Lines).Include(s => s.Payments).SingleAsync();
        saved.Status.ShouldBe(SaleStatus.Completed); saved.Total.MinorUnits.ShouldBe(18000); saved.ChangeGiven.MinorUnits.ShouldBe(2000);
        saved.ReceiptNumber!.Value.ToString().ShouldBe("T1-000007");
        var old = saved.Lines.Single(); old.ReceiptNameSnapshot.ShouldBe("පැරණි තේ"); old.AppliedPriceBand.ShouldBe(1);
        old.UnitCost.MinorUnits.ShouldBe(5000); old.UnitPrice.MinorUnits.ShouldBe(9000);
        old.ManualPriceReason.ShouldBeNull(); old.ManualPriceApprovedBy.ShouldBeNull(); old.NormalUnitPriceMinor.ShouldBeNull();
        Should.Throw<DomainException>(() => saved.ChangeLineQuantity(old.Id, Quantity.One));
    }

    [Fact]
    public async Task Checkout_rejects_drawer_overflow_before_allocating_a_receipt_or_posting_stock()
    {
        await using var f = new LocalFixture(); await f.SetupAsync(); await BillingProduct(f, 150);
        await Billing(f).OpenShiftAsync(Guid.NewGuid(), Money.FromMinor(long.MaxValue, Currency.FromCode("LKR")).ToDecimal());
        var state = await Scan(f, await Billing(f).LoadAsync());
        (await Should.ThrowAsync<FoundationException>(() => Billing(f).CompleteAsync(Cash(state, 200)))).Key.ShouldBe("BillingAmountTooLarge");
        await using var scope = f.Provider.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<TillDbContext>();
        (await db.Sales.IgnoreQueryFilters().CountAsync()).ShouldBe(0);
        (await db.ReceiptCounters.CountAsync()).ShouldBe(0);
        (await db.StockMovements.IgnoreQueryFilters().CountAsync(m => m.SourceType == "Sale")).ShouldBe(0);
    }

    [Fact]
    public async Task Five_tier_items_and_manual_weight_complete_as_one_cash_bill_with_exact_snapshots_and_stock()
    {
        await using var f = new LocalFixture(); await f.SetupAsync(); var tea = await BillingProduct(f);
        var category = await Catalogue(f).SaveCategoryAsync(null, "එළවළු", "Vegetables", true);
        var weighed = await Catalogue(f).SaveProductAsync(Tea(category) with { Sku = "WEIGHED", Barcode = "00009",
            Unit = UnitOfMeasure.Kilogram, NormalPrice = 150, Bands = [] });
        await Stock(f).PostAsync(new(Guid.NewGuid(), weighed, StockEntryKind.Opening, 10, 50, "Opening", f.Clock.UtcNow));
        await Billing(f).OpenShiftAsync(Guid.NewGuid(), 1000);
        var state = await Billing(f).LoadAsync();
        for (var i = 0; i < 5; i++) state = await Scan(f, state);
        state = await Billing(f).ScanAsync(state.Basket.Id, state.Basket.Version, "00009", 1.250m);
        state.Quote.TotalMinor.ShouldBe(58750); state.Quote.CashDueMinor.ShouldBe(58800);
        var result = await Billing(f).CompleteAsync(Cash(state, 600));
        result.ChangeMinor.ShouldBe(1200); result.RoundingMinor.ShouldBe(50);
        (await Billing(f).LoadAsync()).Shift!.ExpectedCash.ShouldBe(1588);
        var stock = await Stock(f).LoadAsync();
        stock.Products.Single(p => p.Id == tea).Balance.ShouldBe(95);
        stock.Products.Single(p => p.Id == weighed).Balance.ShouldBe(8.750m);
        await using var scope = f.Provider.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<TillDbContext>();
        var sale = await db.Sales.IgnoreQueryFilters().Include(s => s.Lines).SingleAsync();
        sale.Lines.Count.ShouldBe(2);
        var tier = sale.Lines.Single(l => l.ProductId == tea);
        tier.Quantity.Thousandths.ShouldBe(5000); tier.UnitPrice.MinorUnits.ShouldBe(8000); tier.AppliedPriceBand.ShouldBe(3);
        sale.Lines.Single(l => l.ProductId == weighed).Quantity.Thousandths.ShouldBe(1250);
    }

}
