using Microsoft.Data.Sqlite;
using Npgsql;
using Microsoft.EntityFrameworkCore;
using RetailCore.Application.Abstractions;
using RetailCore.Application.Inventory;
using RetailCore.Application.Security;
using RetailCore.Domain.Common;
using RetailCore.Infrastructure.Persistence;
using RetailCore.Infrastructure.Postgres;
using RetailCore.Infrastructure.Sqlite;
using Testcontainers.PostgreSql;

namespace RetailCore.Integration.Tests;

/// <summary>
/// One database to run a test against, without the test caring which provider it is.
/// </summary>
/// <remarks>
/// The whole point of Phase 1 is that the two providers behave the same, so the tests are
/// written once and run twice.
/// </remarks>
internal interface IDatabaseHarness : IAsyncDisposable
{
    string Provider { get; }

    TestTenantContext Tenant { get; }

    RetailCoreDbContext NewContext();
}

/// <summary>
/// Wires up the dispatcher by hand rather than through the container.
/// </summary>
/// <remarks>
/// The handlers under test are the ones registered in
/// <c>DependencyInjection.AddRetailCoreEventHandlers</c>. Constructing them here keeps the
/// harness free of a service provider per context, and makes it obvious in the test what runs
/// when a sale completes.
/// </remarks>
internal sealed class TestEventDispatcher : IDomainEventDispatcher
{
    private readonly List<Func<RetailCoreDbContext, IDomainEvent, Task>> _handlers = [];

    public RetailCoreDbContext? Context { get; set; }

    public static TestEventDispatcher Standard()
    {
        var dispatcher = new TestEventDispatcher();

        dispatcher.Register<Domain.Sales.SaleCompleted>((ctx, e) =>
            new PostStockForCompletedSale(new StockLedger(ctx)).HandleAsync(e));
        dispatcher.Register<Domain.Sales.SaleVoided>((ctx, e) =>
            new PostStockForVoidedSale(new StockLedger(ctx)).HandleAsync(e));
        dispatcher.Register<Domain.Sales.RefundIssued>((ctx, e) =>
            new PostStockForRefund(new StockLedger(ctx)).HandleAsync(e));

        dispatcher.Register<Domain.Sales.SaleVoided>((ctx, e) =>
            new AuditSaleVoided(new AuditLog(ctx)).HandleAsync(e));
        dispatcher.Register<Domain.Sales.SaleLineVoided>((ctx, e) =>
            new AuditSaleLineVoided(new AuditLog(ctx)).HandleAsync(e));
        dispatcher.Register<Domain.Sales.DiscountApplied>((ctx, e) =>
            new AuditDiscountApplied(new AuditLog(ctx)).HandleAsync(e));
        dispatcher.Register<Domain.Sales.RefundIssued>((ctx, e) =>
            new AuditRefundIssued(new AuditLog(ctx)).HandleAsync(e));

        return dispatcher;
    }

    public async Task DispatchAsync(
        IReadOnlyList<IDomainEvent> events,
        CancellationToken cancellationToken = default)
    {
        if (Context is null)
        {
            return;
        }

        foreach (var domainEvent in events)
        {
            foreach (var handler in _handlers)
            {
                await handler(Context, domainEvent).ConfigureAwait(false);
            }
        }
    }

    private void Register<TEvent>(Func<RetailCoreDbContext, TEvent, Task> handler)
        where TEvent : IDomainEvent =>
        _handlers.Add((ctx, e) => e is TEvent typed ? handler(ctx, typed) : Task.CompletedTask);
}

/// <summary>The till's database, in a file so that write-ahead logging behaves as it does in a shop.</summary>
internal sealed class SqliteHarness : IDatabaseHarness
{
    private readonly string _path;
    private readonly SqliteConnection _keepAlive;

    private SqliteHarness(string path, SqliteConnection keepAlive)
    {
        _path = path;
        _keepAlive = keepAlive;
    }

    public string Provider => "SQLite";

    public TestTenantContext Tenant { get; } = new();

    public static async Task<SqliteHarness> CreateAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"retailcore-{Guid.CreateVersion7():N}.db");
        var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync().ConfigureAwait(false);

        var harness = new SqliteHarness(path, connection);

        await using var context = harness.NewContext();
        await context.Database.MigrateAsync().ConfigureAwait(false);

        return harness;
    }

    public RetailCoreDbContext NewContext()
    {
        var dispatcher = TestEventDispatcher.Standard();

        var options = new DbContextOptionsBuilder<TillDbContext>()
            .UseSqlite($"Data Source={_path}")
            .Options;

        var context = new TillDbContext(options, Tenant, dispatcher);
        dispatcher.Context = context;
        return context;
    }

    public async ValueTask DisposeAsync()
    {
        await _keepAlive.DisposeAsync().ConfigureAwait(false);
        SqliteConnection.ClearAllPools();

        try
        {
            File.Delete(_path);
        }
        catch (IOException)
        {
            // A leftover temp file is not worth failing a test run over.
        }
    }
}

/// <summary>
/// The central database, in a real PostgreSQL container.
/// </summary>
/// <remarks>
/// PLAN.md Phase 1 specifies Testcontainers with a real PostgreSQL rather than an in-memory
/// stand-in, and row level security is exactly why: it does not exist in any fake, so a test
/// against one would prove nothing about the control that actually protects tenants.
/// </remarks>
internal sealed class PostgresHarness : IDatabaseHarness
{
    /// <summary>
    /// The password for the throwaway application role in the test container. Not a secret:
    /// the container is created and destroyed inside this test run and listens on an
    /// ephemeral local port.
    /// </summary>
    private const string AppRolePassword = "retailcore-test";

    private readonly PostgreSqlContainer _container;
    private readonly string _appConnectionString;

    private PostgresHarness(PostgreSqlContainer container, string appConnectionString)
    {
        _container = container;
        _appConnectionString = appConnectionString;
    }

    public string Provider => "PostgreSQL";

    public TestTenantContext Tenant { get; } = new();

    public static async Task<PostgresHarness> CreateAsync()
    {
        var container = new PostgreSqlBuilder("postgres:17-alpine").Build();

        await container.StartAsync().ConfigureAwait(false);

        var adminConnectionString = container.GetConnectionString();

        // Migrations run as the owner, as they do in deployment.
        await using (var migrationContext = ContextFor(adminConnectionString, new TestTenantContext()))
        {
            await migrationContext.Database.MigrateAsync().ConfigureAwait(false);
        }

        // Everything after this connects as the unprivileged application role the migration
        // created. This is the whole point: PostgreSQL exempts superusers from row level
        // security silently, so a test that connected as the container's postgres user would
        // pass while proving nothing. The application must never connect as an owner either.
        await using (var connection = new NpgsqlConnection(adminConnectionString))
        {
            await connection.OpenAsync().ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText =
                $"ALTER ROLE retailcore_app LOGIN PASSWORD '{AppRolePassword}';";
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        var appConnectionString = new NpgsqlConnectionStringBuilder(adminConnectionString)
        {
            Username = "retailcore_app",
            Password = AppRolePassword,
        }.ConnectionString;

        return new PostgresHarness(container, appConnectionString);
    }

    private static CentralDbContext ContextFor(
        string connectionString,
        TestTenantContext tenant,
        IDomainEventDispatcher? dispatcher = null)
    {
        var options = new DbContextOptionsBuilder<CentralDbContext>()
            .UseNpgsql(connectionString)
            .AddInterceptors(
                new TenantConnectionInterceptor(tenant),
                new TenantTransactionInterceptor(tenant))
            .Options;

        return new CentralDbContext(options, tenant, dispatcher);
    }

    public RetailCoreDbContext NewContext()
    {
        var dispatcher = TestEventDispatcher.Standard();
        var context = ContextFor(_appConnectionString, Tenant, dispatcher);
        dispatcher.Context = context;
        return context;
    }

    /// <summary>
    /// A connection that has not said which shop it is, to prove the policies fail closed.
    /// TEN-04.
    /// </summary>
    public RetailCoreDbContext NewContextWithoutTenant() =>
        ContextFor(_appConnectionString, new TestTenantContext());

    public async ValueTask DisposeAsync() => await _container.DisposeAsync().ConfigureAwait(false);
}
