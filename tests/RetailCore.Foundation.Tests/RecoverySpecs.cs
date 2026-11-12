using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using RetailCore.Application.Foundation;
using RetailCore.Application.Recovery;
using RetailCore.Domain.Security;
using RetailCore.Infrastructure.Sqlite;
using RetailCore.Infrastructure.Sqlite.Recovery;
namespace RetailCore.Foundation.Tests;

public sealed partial class FoundationSpecs
{
    private static RecoveryService Recovery(LocalFixture f) => f.Provider.GetRequiredService<RecoveryService>();
    private static async Task<ApprovalTicket> RestoreApproval(LocalFixture f, RestoreRequest request) => await f.App.ApproveAsync((await f.App.ApproversAsync()).Single().Id, Pin, RecoveryService.RestoreApproval(request));
    private sealed class RecoveryFault : IRecoveryFaults
    {
        public string? Stage { get; set; }
        public void At(string stage) { if (Stage == stage) { Stage = null; throw new InvalidOperationException("Injected maintenance interruption"); } }
    }
    private static async Task CloseForRestore(LocalFixture f)
    {
        var request = await CashCommand(f, 0, close: true); await Drawer(f).PostAsync(request, await CashApproval(f, request));
    }
    [Fact]
    public async Task Live_backup_includes_WAL_bills_stock_cash_and_restore_requires_exact_owner_approval()
    {
        await using var f = new LocalFixture(); var owner = await f.SetupAsync(); await BillingProduct(f, 150); await Billing(f).OpenShiftAsync(Guid.NewGuid(), 1000);
        var basket = await Scan(f, await Billing(f).LoadAsync()); var bill = await Billing(f).CompleteAsync(Cash(basket, 200));
        var backup = await Recovery(f).BackupAsync(); backup.State.ShouldBe("BackupPrimaryOnly"); File.Exists(backup.LastFile).ShouldBeTrue();
        var review = await Recovery(f).ReviewAsync(backup.LastFile!); review.Bills.ShouldBe(1); review.Movements.ShouldBe(2); review.Shifts.ShouldBe(1);
        var request = new RestoreRequest(Guid.NewGuid(), review.Path, review.Sha256, "Verified shop recovery");
        (await Should.ThrowAsync<FoundationException>(async () => await Recovery(f).RestoreAsync(request, await RestoreApproval(f, request)))).Key.ShouldBe("RestoreOpenShift");
        await CloseForRestore(f);
        (await Should.ThrowAsync<FoundationException>(() => Recovery(f).RestoreAsync(request, null))).Key.ShouldBe("ApprovalRequired");
        var input = (await Catalogue(f).LoadAsync()).Products.Single().Product; await Catalogue(f).SaveProductAsync(input with { NormalPrice = 999 });
        await Recovery(f).RestoreAsync(request, await RestoreApproval(f, request)); f.Provider.GetRequiredService<LocalSession>().IsLocked.ShouldBeTrue();
        await f.RestartAsync(); await f.App.StartAsync(); await f.App.SignInAsync(owner.Id, Pin);
        (await Billing(f).LoadAsync()).Shift!.ExpectedCash.ShouldBe(1150);
        (await Catalogue(f).LoadAsync()).Products.Single().NormalPrice.ShouldBe(150);
        (await f.Provider.GetRequiredService<RetailCore.Application.Receipts.ReceiptService>().DetailAsync(bill.SaleId)).Receipt.Number.ShouldBe(bill.Receipt);
        (await Stock(f).LoadAsync()).Products.Single().Balance.ShouldBe(99);
        await using var scope = f.Provider.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<TillDbContext>();
        (await db.Sales.IgnoreQueryFilters().CountAsync()).ShouldBe(1); (await db.StockMovements.IgnoreQueryFilters().CountAsync()).ShouldBe(2);
        (await db.AuditEntries.IgnoreQueryFilters().CountAsync(a => a.EntityType == "DatabaseRestore")).ShouldBe(1);
    }
    [Theory]
    [InlineData("RestorePrepared", false)]
    [InlineData("RestoreCopied", false)]
    [InlineData("RestoreCommitted", true)]
    public async Task Interrupted_restore_blocks_trading_and_startup_keeps_only_a_verified_complete_state(string stage, bool committed)
    {
        var fault = new RecoveryFault(); await using var f = new LocalFixture(recoveryFault: fault); var owner = await f.SetupAsync(); await BillingProduct(f, 100);
        var backup = await Recovery(f).BackupAsync(); var review = await Recovery(f).ReviewAsync(backup.LastFile!);
        var p = (await Catalogue(f).LoadAsync()).Products.Single().Product; await Catalogue(f).SaveProductAsync(p with { NormalPrice = 777, Bands = [] });
        var request = new RestoreRequest(Guid.NewGuid(), review.Path, review.Sha256, "Power loss rehearsal"); fault.Stage = stage;
        await Should.ThrowAsync<InvalidOperationException>(async () => await Recovery(f).RestoreAsync(request, await RestoreApproval(f, request)));
        (await Should.ThrowAsync<FoundationException>(() => f.Transactions.ReadAsync((_, _) => Task.FromResult(1)))).Key.ShouldBe("RecoveryPending");
        await f.RestartAsync(); await f.App.StartAsync(); await f.App.SignInAsync(owner.Id, Pin);
        (await Catalogue(f).LoadAsync()).Products.Single().NormalPrice.ShouldBe(committed ? 100 : 777);
        var status = await Recovery(f).StatusAsync(); status.State.ShouldBe(committed ? "RestoreSucceeded" : "RecoveryRolledBack");
        Directory.GetFiles(Path.Combine(status.Settings.PrimaryDirectory, "safety"), "*.db").Length.ShouldBe(1);
    }
    [Fact]
    public async Task Corrupt_future_modified_and_changed_backups_are_rejected_without_losing_current_data()
    {
        await using var f = new LocalFixture(); var owner = await f.SetupAsync(); await BillingProduct(f);
        var backup = await Recovery(f).BackupAsync(); var review = await Recovery(f).ReviewAsync(backup.LastFile!);
        var copy = Path.Combine(f.DirectoryPath, "bad.db"); File.WriteAllText(copy, "not SQLite");
        (await Should.ThrowAsync<FoundationException>(() => Recovery(f).ReviewAsync(copy))).Key.ShouldBe("InvalidBackup");
        File.Copy(backup.LastFile!, copy, true);
        await using (var connection = new SqliteConnection("Data Source=" + copy + ";Pooling=False"))
        {
            await connection.OpenAsync(); using var command = connection.CreateCommand(); command.CommandText = "DROP TRIGGER AuditEntries_NoUpdate"; await command.ExecuteNonQueryAsync();
        }
        (await Should.ThrowAsync<FoundationException>(() => Recovery(f).ReviewAsync(copy))).Key.ShouldBe("BackupSchemaIncompatible");
        File.Copy(backup.LastFile!, copy, true);
        await using (var connection = new SqliteConnection("Data Source=" + copy + ";Pooling=False"))
        {
            await connection.OpenAsync(); using var command = connection.CreateCommand(); command.CommandText = "INSERT INTO __EFMigrationsHistory VALUES('99999999999999_Future','99.0')"; await command.ExecuteNonQueryAsync();
        }
        (await Should.ThrowAsync<FoundationException>(() => Recovery(f).ReviewAsync(copy))).Key.ShouldBe("BackupSchemaIncompatible");
        var changed = Path.Combine(f.DirectoryPath, "changed.db"); File.Copy(backup.LastFile!, changed); var old = await Recovery(f).ReviewAsync(changed);
        await using (var connection = new SqliteConnection("Data Source=" + changed + ";Pooling=False"))
        { await connection.OpenAsync(); using var command = connection.CreateCommand(); command.CommandText = "UPDATE Shops SET Name='Other name'"; await command.ExecuteNonQueryAsync(); }
        var request = new RestoreRequest(Guid.NewGuid(), old.Path, old.Sha256, "Reason");
        (await Should.ThrowAsync<FoundationException>(async () => await Recovery(f).RestoreAsync(request, await RestoreApproval(f, request)))).Key.ShouldBe("RestoreFileChanged");
        await f.App.SignInAsync(owner.Id, Pin); (await Catalogue(f).LoadAsync()).Products.Single().NormalPrice.ShouldBe(100);
    }
    [Fact]
    public async Task Daily_backup_retries_missing_USB_once_available_retains_copies_and_cashiers_cannot_maintain()
    {
        await using var f = new LocalFixture(); var owner = await f.SetupAsync();
        var initial = await Recovery(f).StatusAsync(); var usb = Path.Combine(Path.GetTempPath(), "retailcore-usb-" + Guid.NewGuid().ToString("N"));
        try
        {
            await Recovery(f).ConfigureAsync(new(initial.Settings.PrimaryDirectory, usb, 2));
            var engine = f.Provider.GetRequiredService<SqliteRecovery>(); var failed = await engine.DailyAsync(); failed.State.ShouldBe("BackupSecondFailed"); File.Exists(failed.LastFile).ShouldBeTrue();
            Directory.Exists(usb).ShouldBeFalse(); Directory.CreateDirectory(usb);
            var success = await engine.DailyAsync(); success.State.ShouldBe("BackupSucceeded");success.LastFile.ShouldBe(failed.LastFile); File.Exists(Path.Combine(usb, Path.GetFileName(success.LastFile!))).ShouldBeTrue();
            (await engine.DailyAsync()).LastFile.ShouldBe(success.LastFile);
            await Recovery(f).ConfigureAsync(success.Settings);
            (await engine.DailyAsync()).LastFile.ShouldNotBe(success.LastFile);
            for (var i = 0; i < 3; i++) { f.Clock.UtcNow = f.Clock.UtcNow.AddDays(1); await engine.DailyAsync(); }
            Directory.GetFiles(initial.Settings.PrimaryDirectory, "retailcore-*.db").Length.ShouldBe(2); Directory.GetFiles(usb, "retailcore-*.db").Length.ShouldBe(2);
            await f.App.CreateUserAsync("Cashier", "654321", Role.Cashier, "en"); var cashier = (await f.App.UsersAsync()).Single(u => u.Role == Role.Cashier);
            f.App.SignOut(); await f.App.SignInAsync(cashier.Id, "654321");
            (await Should.ThrowAsync<FoundationException>(() => Recovery(f).BackupAsync())).Key.ShouldBe("OwnerRequired");
            (await Should.ThrowAsync<FoundationException>(() => Recovery(f).ConfigureAsync(initial.Settings))).Key.ShouldBe("OwnerRequired");
            (await Should.ThrowAsync<FoundationException>(() => Recovery(f).ReviewAsync(success.LastFile!))).Key.ShouldBe("OwnerRequired");
        }
        finally { if (Directory.Exists(usb)) Directory.Delete(usb, true); }
    }
    [Fact]
    public async Task Backup_restores_bills_stock_and_identity_into_a_clean_checkout_profile()
    {
        await using var source = new LocalFixture(); var sourceOwner = await source.SetupAsync(); await BillingProduct(source, 150); await Billing(source).OpenShiftAsync(Guid.NewGuid(), 1000);
        var basket = await Scan(source, await Billing(source).LoadAsync()); var bill = await Billing(source).CompleteAsync(Cash(basket, 200));
        var backup = await Recovery(source).BackupAsync();
        await using var target = new LocalFixture(); await target.SetupAsync();
        var review = await Recovery(target).ReviewAsync(backup.LastFile!); var request = new RestoreRequest(Guid.NewGuid(), review.Path, review.Sha256, "Clean replacement checkout");
        await Recovery(target).RestoreAsync(request, await RestoreApproval(target, request));
        var info = await target.App.StartAsync(); info.Users.Single().Id.ShouldBe(sourceOwner.Id); await target.App.SignInAsync(sourceOwner.Id, Pin);
        var history=(await Reports(target).LoadAsync("TradingAudit",Day)).Rows.Single(r=>(string?)r[1]=="AuditDatabaseRestored");history[2].ShouldBe("Owner");history[3].ShouldBe("Owner");
        (await Billing(target).LoadAsync()).Shift!.ExpectedCash.ShouldBe(1150); (await Stock(target).LoadAsync()).Products.Single().Balance.ShouldBe(99);
        (await target.Provider.GetRequiredService<RetailCore.Application.Receipts.ReceiptService>().DetailAsync(bill.SaleId)).Receipt.Number.ShouldBe(bill.Receipt);
    }
    private sealed class PauseRecovery : IRecoveryFaults, IDisposable
    {
        public ManualResetEventSlim Entered { get; } = new(); public ManualResetEventSlim Release { get; } = new();
        public void At(string stage) { if (stage == "RestorePrepared") { Entered.Set(); if (!Release.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("Test did not release restore"); } }
        public void Dispose() { Entered.Dispose(); Release.Dispose(); }
    }
    [Fact]
    public async Task Restore_excludes_other_local_transactions_until_a_complete_database_is_available()
    {
        using var pause = new PauseRecovery(); await using var f = new LocalFixture(recoveryFault: pause); await f.SetupAsync();
        var saved = await Recovery(f).BackupAsync(); var review = await Recovery(f).ReviewAsync(saved.LastFile!); var request = new RestoreRequest(Guid.NewGuid(), review.Path, review.Sha256, "Exclusive maintenance");
        var approval = await RestoreApproval(f, request); var restore = Task.Run(() => Recovery(f).RestoreAsync(request, approval));
        pause.Entered.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue();
        Task<int>? blocked = null;
        try
        {
            blocked = f.Transactions.ReadAsync((_, _) => Task.FromResult(1)); await Task.Delay(100); blocked.IsCompleted.ShouldBeFalse();
        }
        finally { pause.Release.Set(); }
        await restore; (await blocked!).ShouldBe(1);
    }

    [Fact]
    public async Task Upgrade_is_preceded_by_verified_backup_and_failure_restores_old_schema()
    {
        var fault = new RecoveryFault(); await using var f = new LocalFixture(recoveryFault: fault);
        await using (var scope = f.Provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TillDbContext>(); await db.GetService<IMigrator>().MigrateAsync("20261111182904_ReturnsAndExchanges");
        }
        fault.Stage = "UpgradeMigrated";
        await Should.ThrowAsync<InvalidOperationException>(() => f.App.StartAsync());
        await using (var scope = f.Provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TillDbContext>(); (await db.Database.GetAppliedMigrationsAsync()).Last().ShouldBe("20261111182904_ReturnsAndExchanges");
        }
        var status = JsonStatus(f); Directory.GetFiles(Path.Combine(status.Settings.PrimaryDirectory, "safety"), "*.db").Length.ShouldBe(1);
        (await f.App.StartAsync()).NeedsSetup.ShouldBeTrue();
        await using (var scope = f.Provider.CreateAsyncScope()) (await scope.ServiceProvider.GetRequiredService<TillDbContext>().Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
    }
    private static BackupStatus JsonStatus(LocalFixture f) => System.Text.Json.JsonSerializer.Deserialize<BackupStatus>(File.ReadAllText(Path.Combine(f.DirectoryPath, "till.db.recovery", "status.json")))!;
}
