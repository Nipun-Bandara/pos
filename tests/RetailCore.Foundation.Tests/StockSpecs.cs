using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailCore.Application.Catalogue;
using RetailCore.Application.Foundation;
using RetailCore.Application.Inventory;
using RetailCore.Domain.Catalogue;
using RetailCore.Domain.Common;
using RetailCore.Domain.Inventory;
using RetailCore.Domain.Monetary;
using RetailCore.Domain.Sales;
using RetailCore.Domain.Security;
using RetailCore.Domain.Tax;
using RetailCore.Infrastructure.Persistence;
using RetailCore.Infrastructure.Sqlite;

namespace RetailCore.Foundation.Tests;

public sealed partial class FoundationSpecs
{
    private static StockService Stock(LocalFixture f) => f.Provider.GetRequiredService<StockService>();
    private static async Task<(ProductId Root, ProductId Pack)> StockProducts(LocalFixture f)
    {
        var category = await Catalogue(f).SaveCategoryAsync(null, "සහල්", "Rice", true);
        var root = await Catalogue(f).SaveProductAsync(Tea(category) with { Sku = "RICE", Barcode = "00001",
            Unit = UnitOfMeasure.Kilogram, EnglishName = "Loose rice", SinhalaName = "සහල්", ReceiptName = "සහල්", Bands = [] });
        var pack = await Catalogue(f).SaveProductAsync(Tea(category) with { Sku = "RICE-25", Barcode = "00002",
            Unit = UnitOfMeasure.Pack, EnglishName = "25 kg bag", SinhalaName = "කිලෝ 25 සහල් බෑගය", ReceiptName = "සහල් බෑගය",
            Cost = 2500, NormalPrice = 3000, Bands = [] });
        await Stock(f).ConfigureAsync(new StockConfiguration(root, root, 1, 10));
        await Stock(f).ConfigureAsync(new StockConfiguration(pack, root, 25, 0));
        return (root, pack);
    }
    private static StockEntryInput Entry(LocalFixture f, ProductId product, decimal quantity = 2,
        StockEntryKind kind = StockEntryKind.Receiving) => new(Guid.NewGuid(), product, kind, quantity, 2500,
            "Delivery / ලැබීම", f.Clock.UtcNow, "Supplier 1");
    private static async Task<ApprovalTicket> Approve(LocalFixture f, StockEntryInput input)
    {
        var owner = (await f.App.UsersAsync()).First(u => u.Role == Role.Owner);
        return await f.App.ApproveAsync(owner.Id, Pin, StockService.EntryApproval(input));
    }
    private static SaleCompleted SaleEvent(LocalFixture f, ShopId shop, UserId cashier, params SoldItem[] items) =>
        new(SaleId.New(), shop, TerminalId.New(), ShiftId.New(), cashier, ReceiptNumber.Create("T1", 1),
            Money.FromMajor(3000, Currency.FromCode("LKR")), items, f.Clock.UtcNow);

    [Fact]
    public async Task Receive_two_bags_preview_and_post_mixed_consumption_against_one_balance_then_restart()
    {
        await using var f = new LocalFixture();
        var owner = await f.SetupAsync();
        var (root, pack) = await StockProducts(f);
        var entry = Entry(f, pack);
        (await Stock(f).PostAsync(entry)).Balance.ShouldBe(50);
        var preview = (await Stock(f).PreviewConsumptionAsync([new(root, 1.250m), new(pack, 1)])).Single();
        preview.Before.ShouldBe(50); preview.Consumed.ShouldBe(26.250m); preview.After.ShouldBe(23.750m);
        var catalogue = await Catalogue(f).LoadAsync();
        catalogue.Products.ShouldAllBe(p => p.StockThousandths == 50000);
        var data = await Stock(f).LoadAsync();
        var received = data.History.Single();
        received.InputQuantity.ShouldBe(2); received.Factor.ShouldBe(25); received.SignedBaseQuantity.ShouldBe(50);
        received.InputUnitCost.ShouldBe(2500); received.UnitCost.ShouldBe(100);
        var shop = await f.Transactions.ReadAsync(async (s, ct) => (await s.ShopsAsync(ct)).Single().Id);
        var sale = SaleEvent(f, shop, owner.Id, new SoldItem(root, Quantity.Of(1.250m), root, 1000, UnitOfMeasure.Kilogram,
            Money.FromMajor(50, Currency.FromCode("LKR"))), new SoldItem(pack, Quantity.One, root, 25000, UnitOfMeasure.Kilogram,
            Money.FromMajor(2500, Currency.FromCode("LKR"))));
        var posting = Stock(f).PrepareSalePosting(sale, "", new Dictionary<ProductId, string>());
        await f.Transactions.WriteAsync(async (s, ct) => { await posting(s, ct); return true; });
        await f.Transactions.WriteAsync(async (s, ct) => { await posting(s, ct); return true; });
        (await Stock(f).LoadAsync()).History.Count.ShouldBe(3);
        await f.RestartAsync(); await f.App.StartAsync(); await f.App.SignInAsync(owner.Id, Pin);
        (await Stock(f).LoadAsync()).Products.ShouldAllBe(p => p.Balance == 23.750m);
        (await Catalogue(f).LoadAsync()).Products.ShouldAllBe(p => p.StockThousandths == 23750);
    }

    [Fact]
    public async Task Manual_adjustment_and_negative_stock_require_exact_single_use_owner_approval()
    {
        await using var f = new LocalFixture(); await f.SetupAsync();
        var (root, pack) = await StockProducts(f); await Stock(f).PostAsync(Entry(f, pack));
        var row = (await Stock(f).LoadAsync()).Products.Single(p => p.Id == root);
        var input = Entry(f, root, 51, StockEntryKind.AdjustmentOut) with { ExpectedRevision = row.Revision };
        (await Should.ThrowAsync<FoundationException>(() => Stock(f).PostAsync(input))).Key.ShouldBe("InsufficientStock");
        input = input with { AllowNegative = true };
        (await Should.ThrowAsync<FoundationException>(() => Stock(f).PostAsync(input))).Key.ShouldBe("ApprovalRequired");
        var wrong = await Approve(f, input with { Quantity = 52 });
        (await Should.ThrowAsync<FoundationException>(() => Stock(f).PostAsync(input, wrong))).Key.ShouldBe("ApprovalRequired");
        var ticket = await Approve(f, input);
        var result = await Stock(f).PostAsync(input, ticket);
        result.Balance.ShouldBe(-1);
        var audits = await f.AuditCountAsync();
        (await Stock(f).PostAsync(input, ticket)).ShouldBe(result);
        (await f.AuditCountAsync()).ShouldBe(audits);
        (await Should.ThrowAsync<FoundationException>(() => Stock(f).PostAsync(input with { Quantity = 52 }, ticket)))
            .Key.ShouldBe("OperationConflict");
        var history = (await Stock(f).LoadAsync()).History[^1];
        history.ApprovedBy.ShouldNotBeNull(); history.Reason.ShouldBe(input.Reason);
        var next = input with { OperationId = Guid.NewGuid(), Quantity = 1,
            ExpectedRevision = (await Stock(f).LoadAsync()).Products.Single(p => p.Id == root).Revision };
        (await Should.ThrowAsync<FoundationException>(() => Stock(f).PostAsync(next, ticket))).Key.ShouldBe("ApprovalRequired");
    }

    [Fact]
    public async Task Count_detects_intervening_equal_net_movements_and_zero_count_is_idempotent()
    {
        await using var f = new LocalFixture(); await f.SetupAsync();
        var (root, pack) = await StockProducts(f); await Stock(f).PostAsync(Entry(f, pack));
        var row = (await Stock(f).LoadAsync()).Products.Single(p => p.Id == root);
        var count = Entry(f, root, 49, StockEntryKind.Count) with { ExpectedRevision = row.Revision, ExpectedBalanceThousandths = 50000 };
        var ticket = await Approve(f, count);
        await Stock(f).PostAsync(Entry(f, root, 1));
        var outgoing = Entry(f, root, 1, StockEntryKind.Wastage);
        await Stock(f).PostAsync(outgoing, await Approve(f, outgoing));
        (await Stock(f).LoadAsync()).Products.Single(p => p.Id == root).Balance.ShouldBe(50);
        (await Should.ThrowAsync<FoundationException>(() => Stock(f).PostAsync(count, ticket))).Key.ShouldBe("StaleStockCount");
        row = (await Stock(f).LoadAsync()).Products.Single(p => p.Id == root);
        count = count with { ExpectedRevision = row.Revision };
        var result = await Stock(f).PostAsync(count, await Approve(f, count));
        result.Variance.ShouldBe(-1); result.Balance.ShouldBe(49);
        row = (await Stock(f).LoadAsync()).Products.Single(p => p.Id == root);
        var zero = count with { OperationId = Guid.NewGuid(), Quantity = 49, ExpectedRevision = row.Revision, ExpectedBalanceThousandths = 49000 };
        var before = (await Stock(f).LoadAsync()).History.Count;
        var approved = await Approve(f, zero);
        (await Stock(f).PostAsync(zero, approved)).Variance.ShouldBe(0);
        await Stock(f).PostAsync(zero, approved);
        (await Stock(f).LoadAsync()).History.Count.ShouldBe(before);
    }

    [Fact]
    public async Task Posting_failure_rolls_back_stock_audit_posting_receipt_and_command_then_retry_succeeds_once()
    {
        var failure = new FailAfterSave(); await using var f = new LocalFixture(failure); await f.SetupAsync();
        var (_, pack) = await StockProducts(f);
        var before = await f.AuditCountAsync(); var input = Entry(f, pack);
        failure.Armed = true;
        await Should.ThrowAsync<InvalidOperationException>(() => Stock(f).PostAsync(input));
        (await Stock(f).LoadAsync()).History.ShouldBeEmpty(); (await f.AuditCountAsync()).ShouldBe(before);
        await using (var scope = f.Provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TillDbContext>();
            (await db.Set<StockPostingReceipt>().IgnoreQueryFilters().CountAsync()).ShouldBe(0);
            (await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM LocalCommands").SingleAsync()).ShouldBe(0);
        }
        await Stock(f).PostAsync(input); await Stock(f).PostAsync(input);
        (await Stock(f).LoadAsync()).History.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Cashier_cannot_change_stock_or_reorder_and_locked_or_revoked_owner_cannot_use_existing_grant()
    {
        await using var f = new LocalFixture(); var owner = await f.SetupAsync();
        var (root, pack) = await StockProducts(f);
        var input = Entry(f, root, 1, StockEntryKind.AdjustmentIn);
        var ticket = await Approve(f, input);
        await f.App.CreateUserAsync("Cashier", "123789", Role.Cashier, "en");
        var cashier = (await f.App.UsersAsync()).Single(u => u.Role == Role.Cashier);
        await f.App.SignInAsync(cashier.Id, "123789");
        (await Should.ThrowAsync<FoundationException>(() => Stock(f).PostAsync(Entry(f, pack)))).Key.ShouldBe("OwnerRequired");
        (await Should.ThrowAsync<FoundationException>(() => Stock(f).ConfigureAsync(new(root, root, 1, 50)))).Key.ShouldBe("OwnerRequired");
        (await Stock(f).LoadAsync()).Products.Count.ShouldBe(2);
        await f.App.SignInAsync(owner.Id, Pin);
        (await Should.ThrowAsync<FoundationException>(() => Stock(f).PostAsync(input, ticket))).Key.ShouldBe("ApprovalRequired");
        ticket = await Approve(f, input); f.App.Lock();
        (await Should.ThrowAsync<FoundationException>(() => Stock(f).PostAsync(input, ticket))).Key.ShouldBe("SignInRequired");
    }

    [Fact]
    public async Task Mapping_is_fixed_after_movement_and_cycles_and_unit_edits_are_rejected()
    {
        await using var f = new LocalFixture(); await f.SetupAsync();
        var (root, pack) = await StockProducts(f);
        await Should.ThrowAsync<FoundationException>(() => Stock(f).ConfigureAsync(new(root, pack, 1, 0)));
        await Should.ThrowAsync<FoundationException>(() => Stock(f).ConfigureAsync(new(pack, pack, 25, 0)));
        await Stock(f).PostAsync(Entry(f, pack, kind: StockEntryKind.Opening));
        (await Should.ThrowAsync<FoundationException>(() => Stock(f).PostAsync(Entry(f, root, kind: StockEntryKind.Opening))))
            .Key.ShouldBe("OpeningAlreadyRecorded");
        (await Should.ThrowAsync<FoundationException>(() => Stock(f).ConfigureAsync(new(pack, root, 20, 0))))
            .Key.ShouldBe("StockMappingLocked");
        var input = (await Catalogue(f).LoadAsync()).Products.Single(p => p.Product.Id == root).Product;
        await Should.ThrowAsync<FoundationException>(() => Catalogue(f).SaveProductAsync(input with { Unit = UnitOfMeasure.Gram }));
        await Stock(f).ConfigureAsync(new(root, root, 1, 55));
        (await Stock(f).LoadAsync()).Products.Single(p => p.Id == root).LowStock.ShouldBeTrue();
        (await Stock(f).LoadAsync()).Products.Single(p => p.Id == pack).LowStock.ShouldBeFalse();
    }

    [Fact]
    public async Task Negative_sale_contract_rejects_missing_approval_and_accepts_owner_approved_cashier_request()
    {
        await using var f = new LocalFixture(); var owner = await f.SetupAsync();
        var (root, _) = await StockProducts(f);
        await f.App.CreateUserAsync("Cashier", "123789", Role.Cashier, "en");
        var cashier = (await f.App.UsersAsync()).Single(u => u.Role == Role.Cashier);
        await f.App.SignInAsync(cashier.Id, "123789");
        var shop = await f.Transactions.ReadAsync(async (s, ct) => (await s.ShopsAsync(ct)).Single().Id);
        var sale = SaleEvent(f, shop, cashier.Id, new SoldItem(root, Quantity.One, root, 1000, UnitOfMeasure.Kilogram));
        var revision = (await Stock(f).LoadAsync()).Products.Single(p => p.Id == root).Revision;
        var revisions = new Dictionary<ProductId, string> { [root] = revision };
        var unapproved = Stock(f).PrepareSalePosting(sale, "Count mismatch", revisions);
        (await Should.ThrowAsync<FoundationException>(() => f.Transactions.WriteAsync(async (s, ct) => { await unapproved(s, ct); return true; })))
            .Key.ShouldBe("ApprovalRequired");
        var ticket = await f.App.ApproveAsync(owner.Id, Pin, StockService.SaleApproval(sale, "Count mismatch", revisions));
        var approved = Stock(f).PrepareSalePosting(sale, "Count mismatch", revisions, ticket);
        await f.Transactions.WriteAsync(async (s, ct) => { await approved(s, ct); return true; });
        await f.Transactions.WriteAsync(async (s, ct) => { await approved(s, ct); return true; });
        (await Stock(f).LoadAsync()).Products.Single(p => p.Id == root).Balance.ShouldBe(-1);
        (await Stock(f).LoadAsync()).History.Single().ApprovedBy.ShouldBe(owner.Id);
    }

    [Fact]
    public async Task Financial_posting_retries_are_deduplicated_and_damaged_returns_never_restock()
    {
        await using var f = new LocalFixture(); var owner = await f.SetupAsync();
        var (root, pack) = await StockProducts(f); await Stock(f).PostAsync(Entry(f, pack));
        var shop = await f.Transactions.ReadAsync(async (s, ct) => (await s.ShopsAsync(ct)).Single().Id);
        var sale = SaleEvent(f, shop, owner.Id, new SoldItem(pack, Quantity.One, root, 25000, UnitOfMeasure.Kilogram,
            Money.FromMajor(2500, Currency.FromCode("LKR"))));
        async Task Post(SaleCompleted value)
        {
            await using var scope = f.Provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TillDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            await new PostStockForCompletedSale(new StockLedger(db)).HandleAsync(value);
            await db.SaveChangesAsync(); await transaction.CommitAsync();
        }
        await Post(sale); await Post(sale);
        (await Stock(f).LoadAsync()).History.Count.ShouldBe(2);
        await Should.ThrowAsync<FoundationException>(() => Post(sale with { Items = [sale.Items[0] with { Quantity = Quantity.Of(2) }] }));
        var refund = new RefundIssued(RefundId.New(), sale.SaleId, shop, sale.TerminalId, sale.ShiftId, owner.Id,
            sale.Total, "Damaged bag", owner.Id, [sale.Items[0] with { Saleable = false }], f.Clock.UtcNow);
        async Task Return(RefundIssued value)
        {
            await using var scope = f.Provider.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<TillDbContext>();
            await new PostStockForRefund(new StockLedger(db)).HandleAsync(value); await db.SaveChangesAsync();
        }
        await Return(refund); await Return(refund);
        (await Stock(f).LoadAsync()).Products.Single(p => p.Id == root).Balance.ShouldBe(25);
        await Should.ThrowAsync<FoundationException>(() => Return(refund with { Items = [sale.Items[0]] }));
        await Return(refund with { RefundId = RefundId.New(), Items = [sale.Items[0]] });
        (await Stock(f).LoadAsync()).Products.Single(p => p.Id == root).Balance.ShouldBe(50);
        await using var read = f.Provider.CreateAsyncScope(); var context = read.ServiceProvider.GetRequiredService<TillDbContext>();
        await Should.ThrowAsync<SqliteException>(() => context.Database.ExecuteSqlRawAsync("DELETE FROM StockPostingReceipts"));
        await Should.ThrowAsync<SqliteException>(() => context.Database.ExecuteSqlRawAsync("UPDATE StockMovements SET InputQuantityThousandths = 5"));
        await Should.ThrowAsync<SqliteException>(() => context.Database.ExecuteSqlRawAsync("DELETE FROM AuditEntries"));
    }
    [Fact]
    public async Task Linking_a_pack_freezes_its_base_unit_before_the_first_movement()
    {
        await using var f = new LocalFixture(); await f.SetupAsync();
        var category = await Catalogue(f).SaveCategoryAsync(null, "සහල්", "Rice", true);
        var root = await Catalogue(f).SaveProductAsync(Tea(category) with { Unit = UnitOfMeasure.Kilogram, Bands = [] });
        var pack = await Catalogue(f).SaveProductAsync(Tea(category) with { Sku = "BAG", Barcode = "00022", Unit = UnitOfMeasure.Pack, Bands = [] });
        await Stock(f).ConfigureAsync(new(pack, root, 25, 0));
        var product = (await Catalogue(f).LoadAsync()).Products.Single(p => p.Product.Id == root).Product;
        (await Should.ThrowAsync<FoundationException>(() => Catalogue(f).SaveProductAsync(product with { Unit = UnitOfMeasure.Gram })))
            .Key.ShouldBe("StockMappingLocked");
    }

    [Fact]
    public async Task Replaying_a_legacy_void_cannot_duplicate_its_stock_restoration()
    {
        await using var f = new LocalFixture(); var owner = await f.SetupAsync();
        var (root, _) = await StockProducts(f);
        var shop = await f.Transactions.ReadAsync(async (s, ct) => (await s.ShopsAsync(ct)).Single().Id);
        var id = SaleId.New();
        await using (var scope = f.Provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TillDbContext>();
            db.StockMovements.Add(StockMovement.ForVoidedSale(shop, root, Quantity.One, id, f.Clock.UtcNow, owner.Id));
            await db.SaveChangesAsync();
        }
        await using var retry = f.Provider.CreateAsyncScope();
        var context = retry.ServiceProvider.GetRequiredService<TillDbContext>();
        var value = new SaleVoided(id, shop, TerminalId.New(), ReceiptNumber.Create("T1", 1),
            Money.FromMajor(100, Currency.FromCode("LKR")), "Legacy void", owner.Id,
            [new SoldItem(root, Quantity.One, root, 1000, UnitOfMeasure.Kilogram)], f.Clock.UtcNow);
        (await Should.ThrowAsync<FoundationException>(() => new PostStockForVoidedSale(new StockLedger(context)).HandleAsync(value)))
            .Key.ShouldBe("StockPostingConflict");
        (await context.StockMovements.IgnoreQueryFilters().CountAsync()).ShouldBe(1);
    }

}
