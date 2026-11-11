using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailCore.Application.Foundation;
using RetailCore.Application.Receipts;
using RetailCore.Domain.Sales;
using RetailCore.Domain.Monetary;
using RetailCore.Domain.Common;
using RetailCore.Infrastructure.Sqlite;
using RetailCore.Infrastructure.Sqlite.Foundation;

namespace RetailCore.Foundation.Tests;

public sealed partial class FoundationSpecs
{
    private static ReceiptService Receipts(LocalFixture f) => f.Provider.GetRequiredService<ReceiptService>();
    private static async Task ConfigurePrinter(LocalFixture f)
    {
        var owner = (await f.App.CurrentAsync()).Id;
        var profile = (await f.App.ProfileAsync()) with { PrinterName = "Receipt Queue", PaperWidthMillimeters = 72 };
        await f.App.SaveProfileAsync(profile, await f.App.ApproveAsync(owner, Pin, FoundationService.SettingsApproval(profile)));
    }
    private static async Task<Guid> CompleteReceiptBill(LocalFixture f)
    {
        if ((await Billing(f).LoadAsync()).Shift is null) await Billing(f).OpenShiftAsync(Guid.NewGuid(), 0);
        var state = await Scan(f, await Billing(f).LoadAsync());
        return (await Billing(f).CompleteAsync(Cash(state, 200))).SaleId;
    }
    private sealed class RecordingPrinter(bool fail = false, Action? afterSend = null) : IReceiptPrinter
    {
        public List<PrintPayload> Jobs { get; } = [];
        public Task SendAsync(PrintPayload payload, CancellationToken cancellationToken)
        {
            Jobs.Add(payload); afterSend?.Invoke();
            if (fail) throw new InvalidOperationException("Simulated adapter failure; no physical hardware claim.");
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Failed_print_restart_lookup_and_explicit_copy_keep_original_snapshot_and_one_financial_effect()
    {
        await using var f = new LocalFixture(); var owner = await f.SetupAsync(); var product = await BillingProduct(f, 150); await ConfigurePrinter(f);
        var id = await CompleteReceiptBill(f); var original = (await Receipts(f).DetailAsync(id)).Receipt;
        var originalJson = JsonSerializer.Serialize(original);
        var badPrinter = new RecordingPrinter(true); var attempt = Guid.NewGuid();
        (await Receipts(f).PrintAsync(id, attempt, badPrinter)).Status.ShouldBe("PrintFailed");
        badPrinter.Jobs.Single().Attempt.IsCopy.ShouldBeFalse();
        await f.RestartAsync(); await f.App.StartAsync(); await f.App.SignInAsync(owner.Id, Pin);
        var savedProduct = (await Catalogue(f).LoadAsync()).Products.Single().Product;
        await Catalogue(f).SaveProductAsync(savedProduct with { NormalPrice = 999, Cost = 9, ReceiptName = "නව තේ", EnglishName = "Changed" });
        var changed = (await f.App.ProfileAsync()) with { Name = "වෙනස් කඩේ", ReceiptFooter = "වෙනස් පාදකය" };
        await f.App.SaveProfileAsync(changed, await f.App.ApproveAsync(owner.Id, Pin, FoundationService.SettingsApproval(changed)));
        var found = await Receipts(f).FindAsync("t1-1"); JsonSerializer.Serialize(found.Receipt).ShouldBe(originalJson);
        found.Prints.Single().Outcome!.Status.ShouldBe("PrintFailed");
        var good = new RecordingPrinter();
        (await Receipts(f).PrintAsync(id, attempt, good)).Status.ShouldBe("PrintAlreadyRequested"); good.Jobs.ShouldBeEmpty();
        (await Receipts(f).PrintAsync(id, Guid.NewGuid(), good)).Status.ShouldBe("PrintSpoolAccepted");
        good.Jobs.Single().Attempt.IsCopy.ShouldBeTrue(); JsonSerializer.Serialize(good.Jobs.Single().Receipt).ShouldBe(originalJson);
        (await Receipts(f).DetailAsync(id)).Prints.Count.ShouldBe(2);
        await using var scope = f.Provider.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<TillDbContext>();
        (await db.Sales.IgnoreQueryFilters().CountAsync()).ShouldBe(1);
        (await db.Set<Payment>().IgnoreQueryFilters().CountAsync()).ShouldBe(1);
        (await db.StockMovements.IgnoreQueryFilters().CountAsync(m => m.SourceType == "Sale")).ShouldBe(1);
        (await db.ReceiptCounters.SingleAsync()).LastIssued.ShouldBe(1);
        original.Lines.Single().CostMinor.ShouldBe(5000); original.TotalMinor.ShouldBe(15000);
        (await Stock(f).LoadAsync()).Products.Single(p => p.Id == product).Balance.ShouldBe(99);
    }

    [Fact]
    public async Task History_dates_use_inclusive_shop_local_days_and_exact_scan_ignores_date_filter()
    {
        await using var f = new LocalFixture(); await f.SetupAsync(); await BillingProduct(f, 150);
        f.Clock.UtcNow = new DateTimeOffset(2026,11,9,18,29,59,TimeSpan.Zero); var first = await CompleteReceiptBill(f);
        f.Clock.UtcNow = new DateTimeOffset(2026,11,9,18,30,0,TimeSpan.Zero); var second = await CompleteReceiptBill(f);
        var ninth = await Receipts(f).HistoryAsync(new(new(2026,11,9), new(2026,11,9)));
        ninth.Single().SaleId.ShouldBe(first); ninth.Single().LocalTime.ShouldContain("23:59:59 +05:30");
        (await Receipts(f).HistoryAsync(new(new(2026,11,10), new(2026,11,10)))).Single().SaleId.ShouldBe(second);
        (await Receipts(f).HistoryAsync(new(new(2026,11,8), new(2026,11,8)))).ShouldBeEmpty();
        (await Receipts(f).FindAsync("T1-000002")).Receipt.SaleId.ShouldBe(second);
        (await Should.ThrowAsync<FoundationException>(() => Receipts(f).HistoryAsync(new(new(2026,11,10), new(2026,11,9))))).Key.ShouldBe("InvalidBillDates");
        (await Should.ThrowAsync<FoundationException>(() => Receipts(f).HistoryAsync(new(new(2020,1,1), new(2026,1,1))))).Key.ShouldBe("InvalidBillDates");
    }

    [Fact]
    public async Task Interrupted_outcome_is_unknown_and_reusing_attempt_never_resends_to_printer()
    {
        var failure = new FailAfterSave(); await using var f = new LocalFixture(failure); await f.SetupAsync(); await BillingProduct(f,150); await ConfigurePrinter(f);
        var id = await CompleteReceiptBill(f); var attempt = Guid.NewGuid();
        var printer = new RecordingPrinter(afterSend: () => failure.Armed = true);
        await Should.ThrowAsync<InvalidOperationException>(() => Receipts(f).PrintAsync(id, attempt, printer));
        var detail = await Receipts(f).DetailAsync(id); detail.Prints.Single().Outcome.ShouldBeNull();
        (await Receipts(f).PrintAsync(id, attempt, printer)).Status.ShouldBe("PrintAlreadyRequested"); printer.Jobs.Count.ShouldBe(1);
        var next = new RecordingPrinter(); await Receipts(f).PrintAsync(id, Guid.NewGuid(), next); next.Jobs.Single().Attempt.IsCopy.ShouldBeTrue();
    }

    [Fact]
    public async Task Receipt_print_and_audit_records_are_append_only_and_receipt_is_atomic_with_sale()
    {
        var failure = new FailAfterSave(); await using var f = new LocalFixture(failure); await f.SetupAsync(); await BillingProduct(f,150); await ConfigurePrinter(f);
        await Billing(f).OpenShiftAsync(Guid.NewGuid(),0); var cart = await Scan(f, await Billing(f).LoadAsync()); failure.Armed = true;
        await Should.ThrowAsync<InvalidOperationException>(() => Billing(f).CompleteAsync(Cash(cart,200)));
        await using var scope = f.Provider.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<TillDbContext>();
        (await db.Set<ReceiptRecord>().CountAsync()).ShouldBe(0);
        var id = (await Billing(f).CompleteAsync(Cash(cart,200))).SaleId;
        await Receipts(f).PrintAsync(id,Guid.NewGuid(),new RecordingPrinter());
        foreach (var sql in new[] {
            "UPDATE ReceiptDocuments SET DocumentJson='tampered'", "DELETE FROM ReceiptDocuments",
            "UPDATE ReceiptPrintAttempts SET DocumentJson='tampered'", "DELETE FROM ReceiptPrintAttempts",
            "UPDATE ReceiptPrintOutcomes SET DocumentJson='tampered'", "DELETE FROM ReceiptPrintOutcomes" })
            await Should.ThrowAsync<Microsoft.Data.Sqlite.SqliteException>(() => db.Database.ExecuteSqlRawAsync(sql));
    }

    [Fact]
    public async Task Locked_and_revoked_users_cannot_read_or_print_cached_bills_and_missing_printer_has_no_job()
    {
        await using var f = new LocalFixture(); var owner = await f.SetupAsync(); await BillingProduct(f,150); var id = await CompleteReceiptBill(f);
        var printer = new RecordingPrinter();
        (await Should.ThrowAsync<FoundationException>(() => Receipts(f).PrintAsync(id,Guid.NewGuid(),printer))).Key.ShouldBe("PrinterNotConfigured");
        (await Receipts(f).DetailAsync(id)).Prints.ShouldBeEmpty();
        f.App.Lock();
        (await Should.ThrowAsync<FoundationException>(() => Receipts(f).DetailAsync(id))).Key.ShouldBe("SignInRequired");
        (await Should.ThrowAsync<FoundationException>(() => Receipts(f).HistoryAsync(new(new(2026,11,9),new(2026,11,9))))).Key.ShouldBe("SignInRequired");
        (await Should.ThrowAsync<FoundationException>(() => Receipts(f).PrintAsync(id,Guid.NewGuid(),printer))).Key.ShouldBe("SignInRequired");
        printer.Jobs.ShouldBeEmpty(); await f.App.SignInAsync(owner.Id,Pin,unlocking:true);
        (await Should.ThrowAsync<FoundationException>(() => Receipts(f).FindAsync("bad"))).Key.ShouldBe("InvalidBillNumber");
        (await Should.ThrowAsync<FoundationException>(() => Receipts(f).FindAsync("T1-999999"))).Key.ShouldBe("BillNotFound");
    }

    [Fact]
    public async Task Cashier_may_find_and_print_saved_bills_but_cannot_change_printer_and_revocation_blocks_further_access()
    {
        await using var f = new LocalFixture(); var owner = await f.SetupAsync(); await BillingProduct(f,150); await ConfigurePrinter(f);
        var id = await CompleteReceiptBill(f);
        await f.App.CreateUserAsync("Cashier", "123789", RetailCore.Domain.Security.Role.Cashier, "en");
        var cashier = (await f.App.UsersAsync()).Single(u => u.Role == RetailCore.Domain.Security.Role.Cashier);
        f.App.SignOut(); await f.App.SignInAsync(cashier.Id,"123789");
        (await Receipts(f).FindAsync("T1-1")).Receipt.SaleId.ShouldBe(id);
        (await Receipts(f).PrintAsync(id,Guid.NewGuid(),new RecordingPrinter())).Status.ShouldBe("PrintSpoolAccepted");
        var grant = await f.App.ApproveAsync(owner.Id,Pin,FoundationService.SettingsApproval(Profile));
        (await Should.ThrowAsync<FoundationException>(() => f.App.SaveProfileAsync(Profile,grant))).Key.ShouldBe("OwnerRequired");
        await f.Transactions.WriteAsync(async (store,ct) =>
        {
            var shop=(await store.ShopsAsync(ct)).Single();(await store.UsersAsync(shop.Id,ct)).Single(u => u.Id == cashier.Id).Deactivate();return true;
        });
        var printer = new RecordingPrinter();
        (await Should.ThrowAsync<FoundationException>(() => Receipts(f).PrintAsync(id,Guid.NewGuid(),printer))).Key.ShouldBe("SignInRequired");
        (await Should.ThrowAsync<FoundationException>(() => Receipts(f).DetailAsync(id))).Key.ShouldBe("SignInRequired");printer.Jobs.ShouldBeEmpty();
    }

    [Fact]
    public async Task Concurrent_repeated_print_identity_calls_the_adapter_once_and_rejects_conflicting_bill()
    {
        await using var f = new LocalFixture(); await f.SetupAsync(); await BillingProduct(f,150); await ConfigurePrinter(f);
        var first = await CompleteReceiptBill(f); var second = await CompleteReceiptBill(f); var attempt = Guid.NewGuid();
        var printer = new RecordingPrinter();
        await Task.WhenAll(Receipts(f).PrintAsync(first,attempt,printer),Receipts(f).PrintAsync(first,attempt,printer));
        printer.Jobs.Count.ShouldBe(1);
        (await Should.ThrowAsync<FoundationException>(() => Receipts(f).PrintAsync(second,attempt,printer))).Key.ShouldBe("PrintConflict");
    }

    [Fact]
    public async Task Legacy_completed_sale_uses_original_line_names_and_labels_first_view_header_without_repricing()
    {
        await using var f = new LocalFixture(); await f.SetupAsync(); await BillingProduct(f,150); await Billing(f).OpenShiftAsync(Guid.NewGuid(),0);
        var id = await f.Transactions.WriteAsync(async (store,ct) =>
        {
            var shop = (await store.ShopsAsync(ct)).Single(); var terminal = (await store.TerminalsAsync(shop.Id,ct)).Single();
            var shift = (await store.OpenShiftAsync(shop.Id,terminal.Id,ct))!; var actor = (await store.UsersAsync(shop.Id,ct)).Single();
            var sale = Sale.Start(shop.Id,terminal.Id,shift.Id,actor.Id,shop.Currency,f.Clock.UtcNow);
            sale.AddPricedLine((await store.ProductsAsync(shop.Id,ct)).Single(),Quantity.One,(await store.TaxRatesAsync(shop.Id,ct)).Single().ToTreatment());
            sale.AddPayment(PaymentMethod.Card,Money.FromMajor(50,shop.Currency),"AUTH-X",f.Clock.UtcNow);
            sale.AddCashPayment(Money.FromMajor(200,shop.Currency),f.Clock.UtcNow);
            sale.Complete(await store.NextReceiptAsync(shop.Id,terminal.Id,ct),f.Clock.UtcNow);store.Add(sale);return sale.Id.Value;
        });
        var detail = await Receipts(f).DetailAsync(id); detail.Receipt.LegacyHeader.ShouldBeTrue();detail.Receipt.Lines.Single().Name.ShouldBe("තේ පැකට්");
        detail.Receipt.Payments.Count.ShouldBe(2); detail.Receipt.PaidMinor.ShouldBe(15000); detail.Receipt.ChangeMinor.ShouldBe(10000);
        JsonSerializer.Serialize((await Receipts(f).DetailAsync(id)).Receipt).ShouldBe(JsonSerializer.Serialize(detail.Receipt));
    }

    [Fact]
    public async Task Sinhala_layout_is_culture_independent_and_print_copy_does_not_change_original_financial_text()
    {
        await using var f = new LocalFixture(); await f.SetupAsync(); await BillingProduct(f,150); var id = await CompleteReceiptBill(f);
        var d = (await Receipts(f).DetailAsync(id)).Receipt;
        var before = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US"); var en = ReceiptLayout.Build(d,false);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("si-LK"); ReceiptLayout.Build(d,false).ShouldBe(en);
            en.ShouldContain(r => r.Text.Contains("150.00",StringComparison.Ordinal)); en.ShouldContain(r => r.Text.Contains("ඉතිරි මුදල",StringComparison.Ordinal));
            ReceiptLayout.Build(d,true).ShouldContain(r => r.Text == ReceiptLayout.Label("Copy"));
            ReceiptLayout.Build(d,true,SaleStatus.Voided).ShouldContain(r => r.Text == ReceiptLayout.Label("Voided"));
        }
        finally { CultureInfo.CurrentUICulture = before; }
        var barcode = BillBarcode.Encode(d.Number); barcode.Take(10).ShouldAllBe(b => !b);barcode.TakeLast(10).ShouldAllBe(b => !b);
        Should.Throw<FoundationException>(() => BillBarcode.Encode("සිංහල"));
    }
}
