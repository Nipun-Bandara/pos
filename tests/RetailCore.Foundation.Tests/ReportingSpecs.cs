using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailCore.Application.Foundation;
using RetailCore.Application.Reporting;
using RetailCore.Domain.Security;
using RetailCore.Infrastructure.Sqlite;
namespace RetailCore.Foundation.Tests;

public sealed partial class FoundationSpecs
{
    private static CashService Drawer(LocalFixture f) => f.Provider.GetRequiredService<CashService>();
    private static ReportService Reports(LocalFixture f) => f.Provider.GetRequiredService<ReportService>();
    private static readonly ReportPeriod Day = new(new(2026, 11, 9), new(2026, 11, 9));
    private static async Task<ApprovalTicket> CashApproval(LocalFixture f, CashRequest r) => await f.App.ApproveAsync((await f.App.ApproversAsync()).Single().Id, Pin, CashService.Approval(r));
    private static async Task<CashRequest> CashCommand(LocalFixture f, long minor, bool close = false, bool payOut = false)
    { var review = await Drawer(f).ReviewAsync(); return new(Guid.NewGuid(), review.ShiftId, close, minor, payOut, "Physical count / supplier receipt", review.ExpectedMinor); }

    [Fact]
    public async Task Cash_change_pay_in_out_close_are_approved_atomic_idempotent_and_persist_variance()
    {
        var fail = new FailAfterSave(); await using var f = new LocalFixture(fail); var owner = await f.SetupAsync();
        await BillingProduct(f, 150); await Billing(f).OpenShiftAsync(Guid.NewGuid(), 1000);
        var state = await Scan(f, await Billing(f).LoadAsync()); await Billing(f).CompleteAsync(Cash(state, 200));
        (await Drawer(f).ReviewAsync()).ExpectedMinor.ShouldBe(115000);
        var request = await CashCommand(f, 5000);
        (await Should.ThrowAsync<FoundationException>(() => Drawer(f).PostAsync(request, null))).Key.ShouldBe("ApprovalRequired");
        var approved = await CashApproval(f, request); var baseline = await f.AuditCountAsync(); fail.Armed = true;
        await Should.ThrowAsync<InvalidOperationException>(() => Drawer(f).PostAsync(request, approved));
        (await f.AuditCountAsync()).ShouldBe(baseline); (await Drawer(f).ReviewAsync()).ExpectedMinor.ShouldBe(115000);
        var result = await Drawer(f).PostAsync(request, await CashApproval(f, request)); result.ExpectedMinor.ShouldBe(120000);
        (await Drawer(f).PostAsync(request, null)).ShouldBe(result);
        var stale = await CashCommand(f, 1000, payOut: true); var staleTicket = await CashApproval(f, stale);
        var next = await CashCommand(f, 2000, payOut: true); await Drawer(f).PostAsync(next, await CashApproval(f, next));
        (await Should.ThrowAsync<FoundationException>(() => Drawer(f).PostAsync(stale, staleTicket))).Key.ShouldBe("CashReviewChanged");
        var excessive = await CashCommand(f, 999999, payOut: true);
        (await Should.ThrowAsync<FoundationException>(async () => await Drawer(f).PostAsync(excessive, await CashApproval(f, excessive)))).Key.ShouldBe("InsufficientDrawerCash");
        var close = await CashCommand(f, 117500, close: true); var closed = await Drawer(f).PostAsync(close, await CashApproval(f, close));
        closed.ExpectedMinor.ShouldBe(118000); closed.VarianceMinor.ShouldBe(-500);
        await f.RestartAsync(); await f.App.StartAsync(); await f.App.SignInAsync(owner.Id, Pin);
        (await Drawer(f).PostAsync(close, null)).ShouldBe(closed);
        var report = await Reports(f).LoadAsync("TradingShifts", Day); report.Rows.Single()[8].ShouldBe(-5m);
        (await Reports(f).LoadAsync("TradingSettlement", Day)).Rows.Single().ShouldBe(new object?[] { 150m, 0m, 0m, 0m, 0m, 50m, 20m, 180m });
        await using var scope = f.Provider.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<TillDbContext>();
        await Should.ThrowAsync<Microsoft.Data.Sqlite.SqliteException>(() => db.Database.ExecuteSqlRawAsync("UPDATE CashMovements SET Reason='tampered'"));
        await Should.ThrowAsync<Microsoft.Data.Sqlite.SqliteException>(() => db.Database.ExecuteSqlRawAsync("UPDATE Shifts SET ClosingNotes='tampered' WHERE Status='Closed'"));
    }

    [Theory]
    [InlineData(150, 50, 0)]
    [InlineData(80, 0, 20)]
    [InlineData(100, 0, 0)]
    public async Task Exchange_reports_turnover_once_and_only_external_settlement(int replacement, int collect, int payout)
    {
        await using var f = new LocalFixture(); await f.SetupAsync(); await BillingProduct(f); await Billing(f).OpenShiftAsync(Guid.NewGuid(), 1000);
        var original = await CompleteReceiptBill(f); await AddReplacement(f, replacement);
        var r = await ReviewReturn(f, (await FullReturn(f, original, true)) with { TenderedMinor = 10000 }); await Returns(f).CompleteAsync(r.Draft, r.Preview.Hash, r.Ticket);
        var sales = (await Reports(f).LoadAsync("TradingSales", Day)).Rows.Single();
        sales[0].ShouldBe((decimal)(100 + replacement)); sales[2].ShouldBe(100m); sales[3].ShouldBe((decimal)replacement); sales[5].ShouldBe(50m); sales[6].ShouldBe(replacement - 50m);
        var tender = (await Reports(f).LoadAsync("TradingSettlement", Day)).Rows.Single(); tender[0].ShouldBe(100m + collect); tender[2].ShouldBe((decimal)payout); tender[4].ShouldBe((decimal)Math.Min(100, replacement)); tender[7].ShouldBe((decimal)replacement);
        var p = (await Catalogue(f).LoadAsync()).Products[0].Product; await Catalogue(f).SaveProductAsync(p with { Cost = 999, EnglishName = "Changed" });
        (await Reports(f).LoadAsync("TradingSales", Day)).Rows.Single()[6].ShouldBe(replacement - 50m);
        var categories = (await Reports(f).LoadAsync("TradingCategories", Day)).Rows; categories.Sum(row => (decimal)row[6]!).ShouldBe((decimal)replacement);
    }

    [Fact]
    public async Task Report_period_uses_shop_midnight_and_requires_current_owner_session()
    {
        await using var f = new LocalFixture(); await f.SetupAsync(); await BillingProduct(f); await Billing(f).OpenShiftAsync(Guid.NewGuid(), 1000);
        f.Clock.UtcNow = new(2026, 11, 9, 18, 29, 59, TimeSpan.Zero); await CompleteReceiptBill(f);
        f.Clock.UtcNow = new(2026, 11, 9, 18, 30, 0, TimeSpan.Zero); await CompleteReceiptBill(f);
        (await Reports(f).LoadAsync("TradingSales", Day)).Rows.Single()[0].ShouldBe(100m);
        (await Reports(f).LoadAsync("TradingSales", new(new(2026, 11, 10), new(2026, 11, 10)))).Rows.Single()[0].ShouldBe(100m);
        await f.App.CreateUserAsync("Cashier", "654321", Role.Cashier, "en"); var cashier = (await f.App.UsersAsync()).Single(u => u.Role == Role.Cashier).Id;
        f.App.SignOut(); await f.App.SignInAsync(cashier, "654321");
        await Should.ThrowAsync<FoundationException>(() => Reports(f).LoadAsync("TradingSales", Day));
        var command = await CashCommand(f, 100);
        (await Should.ThrowAsync<FoundationException>(() => Drawer(f).PostAsync(command, null))).Key.ShouldBe("ApprovalRequired");
    }

    [Fact]
    public async Task Tiny_partial_returns_on_different_days_report_actual_credits_without_invented_cash_rounding()
    {
        await using var f=new LocalFixture();await f.SetupAsync();await BillingProduct(f,.05m,RetailCore.Domain.Catalogue.UnitOfMeasure.Kilogram);await Billing(f).OpenShiftAsync(Guid.NewGuid(),0);
        var basket=await Scan(f,await Billing(f).LoadAsync());var request=Cash(basket,.05m) with{Method=RetailCore.Domain.Sales.PaymentMethod.Card,CardApproved=true};
        var original=(await Billing(f).CompleteAsync(request)).SaleId;var quantities=new long[]{333,333,334};var expected=new decimal[]{.02m,.02m,.01m};
        for(var i=0;i<3;i++)
        {
            f.Clock.UtcNow=new(2026,11,10+i,8,0,0,TimeSpan.Zero);
            var draft=await FullReturn(f,original);draft=draft with{Items=[draft.Items[0] with{Thousandths=quantities[i]}],RefundCardApproved=true};
            var review=await ReviewReturn(f,draft);await Returns(f).CompleteAsync(review.Draft,review.Preview.Hash,review.Ticket);
            var day=new DateOnly(2026,11,10+i);var row=(await Reports(f).LoadAsync("TradingSales",new(day,day))).Rows.Single();row[2].ShouldBe(expected[i]);row[8].ShouldBe(0m);
        }
        var all=(await Reports(f).LoadAsync("TradingSales",new(new(2026,11,9),new(2026,11,12)))).Rows.Single();all[3].ShouldBe(0m);all[5].ShouldBe(0m);all[6].ShouldBe(0m);
    }

    [Fact]
    public async Task Tier_savings_are_reported_as_discounts_and_partial_returns_keep_historical_margin_and_category()
    {
        await using var f = new LocalFixture(); await f.SetupAsync(); await BillingProduct(f); await Billing(f).OpenShiftAsync(Guid.NewGuid(), 1000);
        var basket = await Scan(f, await Billing(f).LoadAsync(), 3); var sale = await Billing(f).CompleteAsync(Cash(basket, 300));
        var changed = (await Catalogue(f).LoadAsync()).Products.Single().Product;
        var category = await Catalogue(f).SaveCategoryAsync(null, "අලුත්", "Changed", true);
        await Catalogue(f).SaveProductAsync(changed with { CategoryId = category, Cost = 999, NormalPrice = 900, Bands = [] });
        var draft = await FullReturn(f, sale.SaleId); draft = draft with { Items = [draft.Items[0] with { Thousandths = 1000, Disposition = RetailCore.Domain.Sales.ReturnDisposition.Damaged }] };
        var r = await ReviewReturn(f, draft); await Returns(f).CompleteAsync(r.Draft, r.Preview.Hash, r.Ticket);
        var totals = (await Reports(f).LoadAsync("TradingSales", Day)).Rows.Single(); totals[0].ShouldBe(300m); totals[1].ShouldBe(45m); totals[2].ShouldBe(85m); totals[3].ShouldBe(170m); totals[5].ShouldBe(100m); totals[6].ShouldBe(70m);
        var categories = (await Reports(f).LoadAsync("TradingCategories", Day)).Rows; categories.Count.ShouldBe(1); ((string)categories[0][0]!).ShouldNotContain("Changed");
    }

    [Fact]
    public async Task Stock_report_explains_wastage_and_only_values_underlying_balances()
    {
        await using var f = new LocalFixture(); await f.SetupAsync(); var product = await BillingProduct(f);
        var row = (await Stock(f).LoadAsync()).Products.Single();
        var waste = Entry(f, product, 2, RetailCore.Application.Inventory.StockEntryKind.Wastage) with { UnitCost = 50 };
        await Stock(f).PostAsync(waste, await Approve(f, waste));
        var balance = (await Reports(f).LoadAsync("TradingStock", Day)).Rows.Single(); balance[3].ShouldBe(98m); balance[6].ShouldBe(4900m);
        var movement = (await Reports(f).LoadAsync("TradingMovements", Day)).Rows.Single(r => (string?)r[3] == "MovementWastage"); movement[4].ShouldBe(-2m);
        await Stock(f).ConfigureAsync(new(product, product, 1, 99));
        (await Reports(f).LoadAsync("TradingLowStock", Day)).Rows.Count.ShouldBe(1);
        var audit = (await Reports(f).LoadAsync("TradingAudit", Day)).Rows.Single(r => (string?)r[1] == "AuditStockAdjusted"); audit[3].ShouldNotBeNull(); audit[4].ShouldBe(waste.Reason);
    }

    [Fact]
    public void Exports_preserve_Sinhala_literal_barcodes_types_and_neutralize_formula_text()
    {
        var table = new ReportTable("සිංහල වාර්තාව", "2026-11-09 Asia/Colombo · filtered", ["Barcode", "Name", "Amount", "Date"],
            [new object?[] { "001234", " =HYPERLINK(\"bad\")", -1.25m, new DateTime(2026, 11, 9) }, new object?[] { "00009", "තේ, පැකට්", 0.05m, null }]);
        var csv = TableExport.Csv(table); csv.Take(3).ShouldBe(new byte[] { 239, 187, 191 }); var text = Encoding.UTF8.GetString(csv); text.ShouldContain("'001234"); text.ShouldContain("' =HYPERLINK"); text.ShouldContain("තේ, පැකට්"); text.ShouldContain("-1.25");
        using var zip = new ZipArchive(new MemoryStream(TableExport.Xlsx(table))); using var stream = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open(); var sheet = XDocument.Load(stream); XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        if (Environment.GetEnvironmentVariable("RETAILCORE_EXPORT_ACCEPTANCE_DIR") is { } directory)
        {
            Directory.CreateDirectory(directory); File.WriteAllBytes(Path.Combine(directory, "sinhala.csv"), csv); File.WriteAllBytes(Path.Combine(directory, "sinhala.xlsx"), TableExport.Xlsx(table));
        }
        sheet.Descendants(ns + "f").ShouldBeEmpty(); sheet.Descendants(ns + "t").Select(t => t.Value).ShouldContain("001234"); sheet.Descendants(ns + "t").Select(t => t.Value).ShouldContain(" =HYPERLINK(\"bad\")");
        sheet.Descendants(ns + "c").Count(c => (string?)c.Attribute("t") == "n").ShouldBe(2); sheet.Descendants(ns + "c").Count(c => (string?)c.Attribute("s") == "1").ShouldBe(1);
    }
}
