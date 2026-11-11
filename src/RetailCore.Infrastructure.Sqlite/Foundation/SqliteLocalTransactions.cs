using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailCore.Application.Abstractions;
using RetailCore.Application.Foundation;
using RetailCore.Domain.Common;
using RetailCore.Domain.Security;
using RetailCore.Domain.Tenancy;

namespace RetailCore.Infrastructure.Sqlite.Foundation;

/// <summary>Serializes local writers; every busy retry gets a new context and transaction.</summary>
public sealed class SqliteLocalTransactions(IServiceScopeFactory scopes) : ILocalTransactions
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TillDbContext>();
        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<T> ReadAsync<T>(Func<ILocalStore, CancellationToken, Task<T>> query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TillDbContext>();
        await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        return await query(new LocalStore(context), cancellationToken).ConfigureAwait(false);
    }

    public Task<T> WriteAsync<T>(Func<ILocalStore, CancellationToken, Task<T>> command,
        CancellationToken cancellationToken = default) => RunAsync(command, null, "", "", cancellationToken);

    public Task<T> ExecuteOnceAsync<T>(Guid operationId, string kind, string fingerprint,
        Func<ILocalStore, CancellationToken, Task<T>> command, CancellationToken cancellationToken = default)
    {
        if (operationId == Guid.Empty || string.IsNullOrWhiteSpace(kind) || kind.Length > 100
            || string.IsNullOrWhiteSpace(fingerprint) || fingerprint.Length > 256)
        {
            throw new ArgumentException("A command identity, kind and safe fingerprint are required.");
        }

        return RunAsync(command, operationId, kind, fingerprint, cancellationToken);
    }

    private async Task<T> RunAsync<T>(Func<ILocalStore, CancellationToken, Task<T>> command,
        Guid? operationId, string kind, string fingerprint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var context = scope.ServiceProvider.GetRequiredService<TillDbContext>();
                await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
                // Microsoft.Data.Sqlite starts non-deferred transactions by default (BEGIN IMMEDIATE).
                await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
                if (operationId is { } id)
                {
                    var prior = await context.Set<CommandRecord>().SingleOrDefaultAsync(r => r.Id == id, cancellationToken).ConfigureAwait(false);
                    if (prior is not null)
                    {
                        if (prior.Kind != kind || prior.Fingerprint != fingerprint)
                        {
                            throw new FoundationException("OperationConflict");
                        }

                        return JsonSerializer.Deserialize<T>(prior.ResultJson)!;
                    }
                }

                var result = await command(new LocalStore(context), cancellationToken).ConfigureAwait(false);
                if (operationId is { } key)
                {
                    context.Add(new CommandRecord(key, kind, fingerprint, JsonSerializer.Serialize(result)));
                }

                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return result;
            }
            catch (Exception ex) when (attempt < 2 && IsBusy(ex))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50 * (attempt + 1)), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static bool IsBusy(Exception ex) => ex is SqliteException { SqliteErrorCode: 5 or 6 }
        || ex is DbUpdateException { InnerException: SqliteException { SqliteErrorCode: 5 or 6 } };

    private sealed class LocalStore(TillDbContext context) : ILocalStore
    {
        public async Task<IReadOnlyList<Shop>> ShopsAsync(CancellationToken cancellationToken) =>
            await context.Shops.ToListAsync(cancellationToken).ConfigureAwait(false);
        public async Task<IReadOnlyList<Terminal>> TerminalsAsync(ShopId shopId, CancellationToken cancellationToken) =>
            await context.Terminals.IgnoreQueryFilters().Where(t => t.ShopId == shopId).ToListAsync(cancellationToken).ConfigureAwait(false);
        public async Task<IReadOnlyList<User>> UsersAsync(ShopId shopId, CancellationToken cancellationToken) =>
            await context.Users.IgnoreQueryFilters().Where(u => u.ShopId == shopId).OrderBy(u => u.DisplayName)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
        public async Task<IReadOnlyList<RetailCore.Domain.Catalogue.Product>> ProductsAsync(ShopId shop, CancellationToken ct) =>
            await context.Products.IgnoreQueryFilters().Where(p => p.ShopId == shop).ToListAsync(ct).ConfigureAwait(false);
        public async Task<IReadOnlyList<RetailCore.Domain.Catalogue.Category>> CategoriesAsync(ShopId shop, CancellationToken ct) =>
            await context.Categories.IgnoreQueryFilters().Where(p => p.ShopId == shop).ToListAsync(ct).ConfigureAwait(false);
        public async Task<IReadOnlyList<RetailCore.Domain.Tax.TaxRate>> TaxRatesAsync(ShopId shop, CancellationToken ct) =>
            await context.TaxRates.IgnoreQueryFilters().Where(p => p.ShopId == shop).ToListAsync(ct).ConfigureAwait(false);
        public async Task<IReadOnlyDictionary<ProductId, long>> StockAsync(ShopId shop, CancellationToken ct)
        {
            var balances = await context.StockMovements.IgnoreQueryFilters().Where(p => p.ShopId == shop)
                .GroupBy(p => p.ProductId).Select(g => new { Product = g.Key,
                    Balance = g.Sum(p => p.Direction == RetailCore.Domain.Inventory.StockDirection.In
                        ? p.Quantity.Thousandths : -p.Quantity.Thousandths) })
                .ToDictionaryAsync(p => p.Product, p => p.Balance, ct).ConfigureAwait(false);
            foreach (var pending in context.StockMovements.Local.Where(m => context.Entry(m).State == EntityState.Added && m.ShopId == shop))
                balances[pending.ProductId] = checked(balances.GetValueOrDefault(pending.ProductId)
                    + (pending.Direction == RetailCore.Domain.Inventory.StockDirection.In ? pending.Quantity.Thousandths : -pending.Quantity.Thousandths));
            return balances;
        }
        public void Add(RetailCore.Domain.Catalogue.Product product) => context.Products.Add(product);
        public void Add(RetailCore.Domain.Catalogue.Category category) => context.Categories.Add(category);
        public void Add(RetailCore.Domain.Tax.TaxRate rate) => context.TaxRates.Add(rate);
        public async Task<IReadOnlyList<RetailCore.Domain.Inventory.StockMovement>> MovementsAsync(ShopId shop, CancellationToken ct)
        {
            var stored = await context.StockMovements.IgnoreQueryFilters().Where(m => m.ShopId == shop).ToListAsync(ct).ConfigureAwait(false);
            return stored.Concat(context.StockMovements.Local.Where(m => context.Entry(m).State == EntityState.Added && m.ShopId == shop)).ToArray();
        }
        public Task PostStockAsync(ShopId shop, string sourceType, Guid sourceId, IReadOnlyList<RetailCore.Domain.Inventory.StockMovement> movements, CancellationToken ct) =>
            new RetailCore.Infrastructure.Persistence.StockLedger(context).PostAsync(shop, sourceType, sourceId, movements, ct);
        public async Task<CompletedLocalCommand?> CommandAsync(Guid id, CancellationToken ct)
        {
            var record = await context.Set<CommandRecord>().SingleOrDefaultAsync(r => r.Id == id, ct).ConfigureAwait(false);
            return record is null ? null : new CompletedLocalCommand(record.Kind, record.Fingerprint, record.ResultJson);
        }
        public void CompleteCommand(Guid id, string kind, string fingerprint, string result) =>
            context.Add(new CommandRecord(id, kind, fingerprint, result));
        public async Task<RetailCore.Application.Checkout.BasketDocument?> BasketAsync(ShopId shop, TerminalId terminal, UserId user, CancellationToken ct)
        {
            var record = await context.Set<BillingBasketRecord>().IgnoreQueryFilters().SingleOrDefaultAsync(b => b.UserId == user && b.ShopId == shop && b.TerminalId == terminal, ct).ConfigureAwait(false);
            return record is null ? null : JsonSerializer.Deserialize<RetailCore.Application.Checkout.BasketDocument>(record.DocumentJson);
        }
        public void SaveBasket(ShopId shop, TerminalId terminal, UserId user, RetailCore.Application.Checkout.BasketDocument basket)
        {
            var record = context.Set<BillingBasketRecord>().Local.SingleOrDefault(b => b.UserId == user);
            var json = JsonSerializer.Serialize(basket);
            if (record is null) context.Add(new BillingBasketRecord(shop, terminal, user, json));
            else record.Update(json);
        }
        public Task<RetailCore.Domain.Shifts.Shift?> OpenShiftAsync(ShopId shop, TerminalId terminal, CancellationToken ct) =>
            context.Shifts.IgnoreQueryFilters().Include(s => s.CashMovements).SingleOrDefaultAsync(s => s.ShopId == shop && s.TerminalId == terminal && s.Status == RetailCore.Domain.Shifts.ShiftStatus.Open, ct);
        public async Task<RetailCore.Domain.Monetary.Money> ExpectedCashAsync(RetailCore.Domain.Shifts.Shift shift, CancellationToken ct) =>
            shift.ExpectedCashFrom(await new RetailCore.Infrastructure.Persistence.ShiftRepository(context).SalesSummaryAsync(shift.Id, ct).ConfigureAwait(false));
        public Task<RetailCore.Domain.Sales.ReceiptNumber> NextReceiptAsync(ShopId shop, TerminalId terminal, CancellationToken ct) =>
            new RetailCore.Infrastructure.Persistence.ReceiptNumberAllocator(context).NextAsync(shop, terminal, ct);
        public void Add(RetailCore.Domain.Sales.Sale sale) => context.Sales.Add(sale);
        public void Add(RetailCore.Domain.Shifts.Shift shift) => context.Shifts.Add(shift);
        public void Add(Shop shop) => context.Shops.Add(shop);
        public void Add(Terminal terminal) => context.Terminals.Add(terminal);
        public void Add(User user) => context.Users.Add(user);
        public void Audit(AuditEntry entry) => context.AuditEntries.Add(entry);
    }
}
