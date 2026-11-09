using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using RetailCore.Application.Abstractions;
using RetailCore.Application.Foundation;
using RetailCore.Domain.Common;
using RetailCore.Domain.Inventory;
using RetailCore.Domain.Monetary;
using RetailCore.Domain.Security;
using RetailCore.Domain.Tenancy;
using RetailCore.Infrastructure.Sqlite;
using RetailCore.Infrastructure.Sqlite.Foundation;

namespace RetailCore.Foundation.Tests;

public sealed partial class FoundationSpecs
{
    private const string Pin = "638194";
    private static ShopProfile Profile => new("අපේ කඩේ", "Shop", "LKR", "Asia/Colombo", "කොළඹ", "0112345678", "ස්තූතියි", null, 80, 100);

    [Fact]
    public async Task Setup_language_settings_and_identity_survive_a_new_process_scope()
    {
        await using var fixture = new LocalFixture();
        var app = fixture.App;
        (await app.StartAsync()).NeedsSetup.ShouldBeTrue();
        await app.SetupAsync(Profile, "Owner", Pin, "en");
        var owner = (await app.StartAsync()).Users.Single();
        await app.SignInAsync(owner.Id, Pin);
        await app.SetLanguageAsync("si");
        var changed = Profile with { Name = "නව නම", Telephone = "0771234567" };
        var approval = await app.ApproveAsync(owner.Id, Pin, FoundationService.SettingsApproval(changed));
        await app.SaveProfileAsync(changed, approval);
        var identity = await fixture.Transactions.ReadAsync(async (store, ct) =>
            ((await store.ShopsAsync(ct)).Single().Id, (await store.TerminalsAsync((await store.ShopsAsync(ct)).Single().Id, ct)).Single().Id));
        await fixture.RestartAsync();
        var restart = await fixture.App.StartAsync();
        restart.NeedsSetup.ShouldBeFalse();
        restart.Users.Single().Id.ShouldBe(owner.Id);
        restart.Users.Single().Language.ShouldBe("si");
        (await fixture.App.SignInAsync(owner.Id, Pin)).Language.ShouldBe("si");
        (await fixture.App.ProfileAsync()).ShouldBe(changed);
        var after = await fixture.Transactions.ReadAsync(async (store, ct) =>
            ((await store.ShopsAsync(ct)).Single().Id, (await store.TerminalsAsync((await store.ShopsAsync(ct)).Single().Id, ct)).Single().Id));
        after.ShouldBe(identity);
        var ex = await Should.ThrowAsync<FoundationException>(() => fixture.App.SetupAsync(Profile, "Other", Pin, "en"));
        ex.Key.ShouldBe("AlreadyConfigured");
    }

    [Fact]
    public async Task Cashier_cannot_call_owner_commands_and_locked_sessions_cannot_call_any_command()
    {
        await using var fixture = new LocalFixture();
        var owner = await fixture.SetupAsync();
        await fixture.App.CreateUserAsync("Cashier", "123789", Role.Cashier, "si");
        var cashier = (await fixture.App.UsersAsync()).Single(u => u.Role == Role.Cashier);
        fixture.App.SignOut();
        await fixture.App.SignInAsync(cashier.Id, "123789");
        (await Should.ThrowAsync<FoundationException>(() => fixture.App.ProfileAsync())).Key.ShouldBe("OwnerRequired");
        (await Should.ThrowAsync<FoundationException>(() => fixture.App.UsersAsync())).Key.ShouldBe("OwnerRequired");
        (await Should.ThrowAsync<FoundationException>(() => fixture.App.CreateUserAsync("Injected", Pin, Role.Owner, "en"))).Key.ShouldBe("OwnerRequired");
        (await Should.ThrowAsync<FoundationException>(() => fixture.App.UpdateUserAsync(owner.Id, Role.Cashier, false, null, false))).Key.ShouldBe("OwnerRequired");
        var grant = await fixture.App.ApproveAsync(owner.Id, Pin, FoundationService.SettingsApproval(Profile));
        (await Should.ThrowAsync<FoundationException>(() => fixture.App.SaveProfileAsync(Profile, grant))).Key.ShouldBe("OwnerRequired");
        fixture.App.Lock();
        (await Should.ThrowAsync<FoundationException>(() => fixture.App.SetLanguageAsync("en"))).Key.ShouldBe("SignInRequired");
        (await Should.ThrowAsync<FoundationException>(() => fixture.App.SignInAsync(owner.Id, Pin, unlocking: true))).Key.ShouldBe("SignInRequired");
        await fixture.App.SignInAsync(cashier.Id, "123789", unlocking: true);
        (await fixture.App.CurrentAsync()).Id.ShouldBe(cashier.Id);
    }

    [Fact]
    public async Task Failed_attempts_and_lockout_survive_restart_and_expire_using_the_clock()
    {
        await using var fixture = new LocalFixture();
        var owner = await fixture.SetupAsync();
        fixture.App.SignOut();
        for (var i = 0; i < 4; i++)
        {
            (await Should.ThrowAsync<FoundationException>(() => fixture.App.SignInAsync(owner.Id, "000000"))).Key.ShouldBe("InvalidCredentials");
        }

        await fixture.RestartAsync();
        (await Should.ThrowAsync<FoundationException>(() => fixture.App.SignInAsync(owner.Id, "000000"))).Key.ShouldBe("AccountLocked");
        await fixture.RestartAsync();
        (await Should.ThrowAsync<FoundationException>(() => fixture.App.SignInAsync(owner.Id, Pin))).Key.ShouldBe("AccountLocked");
        fixture.Clock.UtcNow += TimeSpan.FromMinutes(15);
        await fixture.App.SignInAsync(owner.Id, Pin);
        await using var scope = fixture.Provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TillDbContext>();
        var user = await context.Users.IgnoreQueryFilters().SingleAsync();
        user.FailedPinAttempts.ShouldBe(0);
        user.LockedUntil.ShouldBeNull();
        (await context.AuditEntries.IgnoreQueryFilters().CountAsync(a => a.Action == AuditAction.UserLockedOut)).ShouldBe(1);
        user.PinHash.ShouldStartWith("$argon2id$v=19$m=65536,t=3,p=1$");
    }

    [Fact]
    public async Task Approval_is_exact_single_use_expiring_and_invalidated_by_lock()
    {
        await using var fixture = new LocalFixture();
        var owner = await fixture.SetupAsync();
        var request = FoundationService.SettingsApproval(Profile);
        var ticket = await fixture.App.ApproveAsync(owner.Id, Pin, request);
        (await Should.ThrowAsync<FoundationException>(() => fixture.App.SaveProfileAsync(Profile with { Name = "Changed" }, ticket))).Key.ShouldBe("ApprovalRequired");
        ticket = await fixture.App.ApproveAsync(owner.Id, Pin, request);
        await fixture.App.SaveProfileAsync(Profile, ticket);
        (await Should.ThrowAsync<FoundationException>(() => fixture.App.SaveProfileAsync(Profile, ticket))).Key.ShouldBe("ApprovalRequired");
        ticket = await fixture.App.ApproveAsync(owner.Id, Pin, request);
        fixture.Clock.UtcNow += TimeSpan.FromMinutes(2);
        (await Should.ThrowAsync<FoundationException>(() => fixture.App.SaveProfileAsync(Profile, ticket))).Key.ShouldBe("ApprovalRequired");
        ticket = await fixture.App.ApproveAsync(owner.Id, Pin, request);
        fixture.App.Lock();
        await fixture.App.SignInAsync(owner.Id, Pin, unlocking: true);
        (await Should.ThrowAsync<FoundationException>(() => fixture.App.SaveProfileAsync(Profile, ticket))).Key.ShouldBe("ApprovalRequired");
        (await Should.ThrowAsync<FoundationException>(() => fixture.App.SaveProfileAsync(Profile, new ApprovalTicket(Guid.NewGuid())))).Key.ShouldBe("ApprovalRequired");
    }

    [Fact]
    public async Task Last_owner_is_preserved_and_disabled_users_cannot_sign_in()
    {
        await using var fixture = new LocalFixture();
        var owner = await fixture.SetupAsync();
        (await Should.ThrowAsync<FoundationException>(() => fixture.App.UpdateUserAsync(owner.Id, Role.Owner, false, null, false))).Key.ShouldBe("LastOwner");
        (await Should.ThrowAsync<FoundationException>(() => fixture.App.UpdateUserAsync(owner.Id, Role.Cashier, true, null, false))).Key.ShouldBe("LastOwner");
        await fixture.App.CreateUserAsync("Second", "328194", Role.Owner, "en");
        var second = (await fixture.App.UsersAsync()).Single(u => u.Id != owner.Id);
        await fixture.App.UpdateUserAsync(second.Id, Role.Owner, false, null, false);
        (await Should.ThrowAsync<FoundationException>(() => fixture.App.SignInAsync(second.Id, "328194"))).Key.ShouldBe("InvalidCredentials");
        (await fixture.App.CurrentAsync()).Id.ShouldBe(owner.Id);
    }

    [Fact]
    public async Task Failure_after_real_SQL_writes_rolls_back_business_and_audit_then_fresh_retry_succeeds()
    {
        var failure = new FailAfterSave();
        await using var fixture = new LocalFixture(failure);
        var owner = await fixture.SetupAsync();
        var baseline = await fixture.AuditCountAsync();
        failure.Armed = true;
        await Should.ThrowAsync<InvalidOperationException>(() => fixture.App.CreateUserAsync("RolledBack", "439182", Role.Cashier, "en"));
        (await fixture.App.UsersAsync()).ShouldHaveSingleItem();
        (await fixture.AuditCountAsync()).ShouldBe(baseline);
        await fixture.App.CreateUserAsync("RolledBack", "439182", Role.Cashier, "en");
        (await fixture.App.UsersAsync()).Count.ShouldBe(2);
        (await fixture.AuditCountAsync()).ShouldBe(baseline + 1);
        (await fixture.App.CurrentAsync()).Id.ShouldBe(owner.Id);
    }

    [Fact]
    public async Task Committed_operation_replay_returns_same_result_without_repeating_effects()
    {
        await using var fixture = new LocalFixture();
        await fixture.SetupAsync();
        var id = Guid.NewGuid();
        var executions = 0;
        async Task<int> Command(ILocalStore store, CancellationToken ct)
        {
            executions++;
            var shop = (await store.ShopsAsync(ct)).Single();
            store.Audit(AuditEntry.Record(shop.Id, AuditAction.StockAdjusted, "Fixture", shop.Id.Value, fixture.Clock.UtcNow));
            return 42;
        }

        var baseline = await fixture.AuditCountAsync();
        (await fixture.Transactions.ExecuteOnceAsync(id, "sample", "safe-hash", Command)).ShouldBe(42);
        await fixture.RestartAsync();
        (await fixture.Transactions.ExecuteOnceAsync(id, "sample", "safe-hash", Command)).ShouldBe(42);
        executions.ShouldBe(1);
        (await fixture.AuditCountAsync()).ShouldBe(baseline + 1);
        (await Should.ThrowAsync<FoundationException>(() => fixture.Transactions.ExecuteOnceAsync(id, "sample", "different-hash", Command))).Key.ShouldBe("OperationConflict");
    }

    [Fact]
    public async Task Every_reopened_connection_has_durable_pragmas_and_no_pending_model_changes()
    {
        await using var fixture = new LocalFixture();
        await fixture.SetupAsync();
        for (var i = 0; i < 2; i++)
        {
            await using var scope = fixture.Provider.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<TillDbContext>();
            context.Database.HasPendingModelChanges().ShouldBeFalse();
            await context.Database.OpenConnectionAsync();
            await using var cmd = context.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode;";
            (await cmd.ExecuteScalarAsync())!.ToString().ShouldBe("wal");
            cmd.CommandText = "PRAGMA synchronous;";
            Convert.ToInt64(await cmd.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture).ShouldBe(2);
            cmd.CommandText = "PRAGMA foreign_keys;";
            Convert.ToInt64(await cmd.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture).ShouldBe(1);
            await context.Database.CloseConnectionAsync();
        }
    }

    [Fact]
    public async Task Audit_and_stock_cannot_be_updated_or_deleted_even_with_direct_SQL()
    {
        await using var fixture = new LocalFixture();
        await fixture.SetupAsync();
        await using var scope = fixture.Provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TillDbContext>();
        var shop = await context.Shops.SingleAsync();
        context.StockMovements.Add(StockMovement.Manual(shop.Id, ProductId.New(), Quantity.One,
            StockDirection.In, StockMovementKind.OpeningBalance, "Opening", UserId.New(), fixture.Clock.UtcNow));
        await context.SaveChangesAsync();
        await Should.ThrowAsync<SqliteException>(() => context.Database.ExecuteSqlRawAsync("UPDATE AuditEntries SET Summary = 'tampered';"));
        await Should.ThrowAsync<SqliteException>(() => context.Database.ExecuteSqlRawAsync("DELETE FROM AuditEntries;"));
        await Should.ThrowAsync<SqliteException>(() => context.Database.ExecuteSqlRawAsync("UPDATE StockMovements SET Reason = 'tampered';"));
        await Should.ThrowAsync<SqliteException>(() => context.Database.ExecuteSqlRawAsync("DELETE FROM StockMovements;"));
    }

    [Fact]
    public async Task Initial_schema_upgrades_without_losing_existing_shop_and_user()
    {
        await using var fixture = new LocalFixture();
        var shopId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using (var scope = fixture.Provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TillDbContext>();
            await context.GetService<IMigrator>().MigrateAsync("20260825044656_InitialTillSchema");
            await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Shops (Id,Name,LegalName,AddressLine2,TimeZoneId,IsActive,CurrencyCode) VALUES ({shopId},{"Existing"},{"Existing"},{"Existing second line"},{"Asia/Colombo"},1,{"LKR"});");
            var terminalId = Guid.NewGuid();
            await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Terminals (Id,ShopId,Code,Name,IsEnrolled,IsActive) VALUES ({terminalId},{shopId},{"T1"},{"T1"},0,1);");
            var hash = new Argon2PinHasher().Hash(Pin);
            await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Users (Id,ShopId,DisplayName,Role,PinHash,FailedPinAttempts,IsActive) VALUES ({userId},{shopId},{"Existing owner"},{"Owner"},{hash},0,1);");
        }

        var info = await fixture.App.StartAsync();
        info.Users.Single().Id.Value.ShouldBe(userId);
        info.Users.Single().Language.ShouldBe("en");
        await fixture.App.SignInAsync(new UserId(userId), Pin);
        var profile = await fixture.App.ProfileAsync();
        profile.Name.ShouldBe("Existing");
        profile.PaperWidthMillimeters.ShouldBe(80);
        profile.CashIncrementMinor.ShouldBe(100);
        var ticket = await fixture.App.ApproveAsync(new UserId(userId), Pin, FoundationService.SettingsApproval(profile));
        await fixture.App.SaveProfileAsync(profile, ticket);
        (await fixture.Transactions.ReadAsync(async (store, ct) => (await store.ShopsAsync(ct)).Single().AddressLine2))
            .ShouldBe("Existing second line");
    }

    [Fact]
    public async Task Resetting_credentials_revokes_other_sessions_and_existing_approvals()
    {
        await using var fixture = new LocalFixture();
        var owner = await fixture.SetupAsync();
        await fixture.App.CreateUserAsync("Second owner", "832194", Role.Owner, "en");
        var second = (await fixture.App.UsersAsync()).Single(u => u.Id != owner.Id);
        var ticket = await fixture.App.ApproveAsync(owner.Id, Pin, FoundationService.SettingsApproval(Profile));
        await using var otherProvider = fixture.AnotherSession();
        var otherApp = otherProvider.GetRequiredService<FoundationService>();
        await otherApp.StartAsync();
        await otherApp.SignInAsync(second.Id, "832194");
        await otherApp.UpdateUserAsync(owner.Id, Role.Owner, true, "998431", false);
        (await Should.ThrowAsync<FoundationException>(() => fixture.App.CurrentAsync())).Key.ShouldBe("SignInRequired");
        await Should.ThrowAsync<FoundationException>(() => fixture.App.SaveProfileAsync(Profile, ticket));
        (await Should.ThrowAsync<FoundationException>(() => fixture.App.SignInAsync(owner.Id, Pin))).Key.ShouldBe("InvalidCredentials");
        await fixture.App.SignInAsync(owner.Id, "998431");
        (await fixture.App.CurrentAsync()).Id.ShouldBe(owner.Id);
        await using var scope = fixture.Provider.CreateAsyncScope();
        var entries = await scope.ServiceProvider.GetRequiredService<TillDbContext>().AuditEntries.IgnoreQueryFilters().ToListAsync();
        entries.ShouldNotContain(e => (e.DetailsJson ?? "").Contains("998431", StringComparison.Ordinal)
            || (e.DetailsJson ?? "").Contains("$argon2id$", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Failed_idempotent_operation_leaves_no_receipt_and_can_be_retried()
    {
        var failure = new FailAfterSave();
        await using var fixture = new LocalFixture(failure);
        await fixture.SetupAsync();
        var id = Guid.NewGuid();
        var baseline = await fixture.AuditCountAsync();
        async Task<int> Command(ILocalStore store, CancellationToken ct)
        {
            var shop = (await store.ShopsAsync(ct)).Single();
            store.Audit(AuditEntry.Record(shop.Id, AuditAction.ShopSettingsChanged, "Shop", shop.Id.Value, fixture.Clock.UtcNow));
            return 7;
        }

        failure.Armed = true;
        await Should.ThrowAsync<InvalidOperationException>(() => fixture.Transactions.ExecuteOnceAsync(id, "fixture", "digest", Command));
        (await fixture.AuditCountAsync()).ShouldBe(baseline);
        (await fixture.Transactions.ExecuteOnceAsync(id, "fixture", "digest", Command)).ShouldBe(7);
        (await fixture.AuditCountAsync()).ShouldBe(baseline + 1);
    }

    [Fact]
    public void Pin_hashing_has_random_salts_and_rejects_invalid_hash_formats()
    {
        var hasher = new Argon2PinHasher();
        var a = hasher.Hash(Pin);
        var b = hasher.Hash(Pin);
        a.ShouldNotBe(b);
        hasher.Verify(Pin, a).ShouldBeTrue();
        // Cross-checked with argon2-cffi's independent Argon2id implementation, salt bytes 0..15.
        hasher.Verify(Pin, "$argon2id$v=19$m=65536,t=3,p=1$AAECAwQFBgcICQoLDA0ODw$wf32gFbyHJVxrE4zQAVpBPsZhqw4FoOPOmmZKvA4878").ShouldBeTrue();
        hasher.Verify("000000", a).ShouldBeFalse();
        hasher.Verify(Pin, "garbage").ShouldBeFalse();
        hasher.Verify(Pin, a.Replace("m=65536", "m=99999999", StringComparison.Ordinal)).ShouldBeFalse();
        hasher.Verify(Pin, a[..a.LastIndexOf('$')] + "$invalid").ShouldBeFalse();
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 11, 9, 8, 0, 0, TimeSpan.Zero);
    }

    private sealed class FailAfterSave : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (Armed) { Armed = false; throw new InvalidOperationException("Injected after SQL write"); }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class LocalFixture : IAsyncDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "retailcore-tests-" + Guid.NewGuid().ToString("N"));
        private readonly FailAfterSave? _failure;
        public TestClock Clock { get; } = new();
        public ServiceProvider Provider { get; private set; }
        public FoundationService App => Provider.GetRequiredService<FoundationService>();
        public ILocalTransactions Transactions => Provider.GetRequiredService<ILocalTransactions>();

        public LocalFixture(FailAfterSave? failure = null)
        {
            _failure = failure;
            Provider = Build();
        }

        private ServiceProvider Build()
        {
            var services = new ServiceCollection();
            services.AddLocalFoundation(Path.Combine(_directory, "till.db"));
            services.AddSingleton<IClock>(Clock);
            if (_failure is not null) { services.AddDbContext<TillDbContext>(o => o.AddInterceptors(_failure)); }
            return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        }

        public async Task<UserSummary> SetupAsync()
        {
            await App.StartAsync();
            await App.SetupAsync(Profile, "Owner", Pin, "en");
            var owner = (await App.StartAsync()).Users.Single();
            await App.SignInAsync(owner.Id, Pin);
            return owner;
        }

        public ServiceProvider AnotherSession() => Build();

        public async Task RestartAsync()
        {
            await Provider.DisposeAsync();
            Provider = Build();
        }

        public async Task<int> AuditCountAsync()
        {
            await using var scope = Provider.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<TillDbContext>().AuditEntries.IgnoreQueryFilters().CountAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Provider.DisposeAsync();
            SqliteConnection.ClearAllPools();
            Directory.Delete(_directory, true);
        }
    }
}
