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
        public Task<RetailCore.Domain.Shifts.ShiftSalesSummary> ShiftSummaryAsync(ShiftId shift, CancellationToken ct) =>
            new RetailCore.Infrastructure.Persistence.ShiftRepository(context).SalesSummaryAsync(shift, ct);
        public async Task<RetailCore.Application.Reporting.TradingRecords> TradingAsync(ShopId shop, CancellationToken ct) => new(
            await context.Sales.IgnoreQueryFilters().Include(s=>s.Lines).Include(s=>s.Payments).Where(s=>s.ShopId==shop && s.CompletedAt!=null).ToListAsync(ct).ConfigureAwait(false),
            await context.Refunds.IgnoreQueryFilters().Include(r=>r.Lines).Where(r=>r.ShopId==shop && r.CompletedAt!=null).ToListAsync(ct).ConfigureAwait(false),
            await context.Shifts.IgnoreQueryFilters().Include(s=>s.CashMovements).Where(s=>s.ShopId==shop).ToListAsync(ct).ConfigureAwait(false),
            await context.AuditEntries.IgnoreQueryFilters().Where(a=>a.ShopId==shop).ToListAsync(ct).ConfigureAwait(false));
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
        public async Task<IReadOnlyList<RetailCore.Application.Receipts.BillSummary>> BillsAsync(ShopId shop, DateTimeOffset from, DateTimeOffset until, string? number, CancellationToken ct)
        {
            // SQLite cannot order/compare DateTimeOffset directly. julianday respects historical offsets.
            var rows = await context.Database.SqlQuery<BillQueryRow>($"SELECT Id AS SaleId, ReceiptNumber AS Number, CompletedAt, TotalMinor, CurrencyCode AS Currency, Status FROM Sales WHERE ShopId={shop.Value} AND ReceiptNumber IS NOT NULL AND CompletedAt IS NOT NULL AND julianday(CompletedAt)>=julianday({from.ToString("O")}) AND julianday(CompletedAt)<julianday({until.ToString("O")}) AND ({number} IS NULL OR ReceiptNumber={number}) ORDER BY julianday(CompletedAt) DESC,ReceiptNumber DESC LIMIT 501")
                .ToListAsync(ct).ConfigureAwait(false);
            if (rows.Count > 500) throw new FoundationException("NarrowBillDates");
            return rows.Select(s => new RetailCore.Application.Receipts.BillSummary(s.SaleId, s.Number, s.CompletedAt,
                    "", s.TotalMinor, s.Currency, Enum.Parse<RetailCore.Domain.Sales.SaleStatus>(s.Status))).ToArray();
        }
        private sealed record BillQueryRow(Guid SaleId, string Number, DateTimeOffset CompletedAt, long TotalMinor, string Currency, string Status);

        public Task<RetailCore.Domain.Sales.Sale?> SavedSaleAsync(ShopId shop, Guid? id, string? number, CancellationToken ct)
        {
            var query = context.Sales.IgnoreQueryFilters().Include(s => s.Lines).Include(s => s.Payments)
                .Where(s => s.ShopId == shop && s.CompletedAt != null && s.ReceiptNumber != null);
            if (id is { } value) query = query.Where(s => s.Id == new SaleId(value));
            else { var receipt = RetailCore.Domain.Sales.ReceiptNumber.Parse(number!); query = query.Where(s => s.ReceiptNumber == receipt); }
            return query.SingleOrDefaultAsync(ct);
        }
        public async Task<RetailCore.Application.Receipts.ReceiptDocument?> ReceiptAsync(ShopId shop, Guid sale, CancellationToken ct)
        {
            var row = await context.Set<ReceiptRecord>().SingleOrDefaultAsync(r => r.ShopId == shop && r.SaleId == new SaleId(sale), ct).ConfigureAwait(false);
            return row is null ? null : JsonSerializer.Deserialize<RetailCore.Application.Receipts.ReceiptDocument>(row.DocumentJson);
        }
        public void AddReceipt(ShopId shop, RetailCore.Application.Receipts.ReceiptDocument receipt) =>
            context.Add(new ReceiptRecord(shop, receipt.SaleId, JsonSerializer.Serialize(receipt)));
        public async Task<IReadOnlyList<RetailCore.Application.Receipts.PrintHistory>> PrintsAsync(ShopId shop, Guid sale, CancellationToken ct)
        {
            var attempts = await context.Set<PrintAttemptRecord>().Where(r => r.ShopId == shop && r.SaleId == new SaleId(sale)).ToListAsync(ct).ConfigureAwait(false);
            var ids = attempts.Select(r => r.Id).ToArray();
            var outcomes = await context.Set<PrintOutcomeRecord>().Where(r => ids.Contains(r.AttemptId)).ToListAsync(ct).ConfigureAwait(false);
            return attempts.Select(r => new RetailCore.Application.Receipts.PrintHistory(
                JsonSerializer.Deserialize<RetailCore.Application.Receipts.PrintAttempt>(r.DocumentJson)!,
                outcomes.Where(o => o.AttemptId == r.Id).Select(o => JsonSerializer.Deserialize<RetailCore.Application.Receipts.PrintOutcome>(o.DocumentJson)).SingleOrDefault()))
                .OrderBy(r => r.Attempt.RequestedAt).ToArray();
        }
        public async Task<RetailCore.Application.Receipts.PrintAttempt?> PrintAttemptAsync(ShopId shop, Guid id, CancellationToken ct)
        {
            var row = await context.Set<PrintAttemptRecord>().SingleOrDefaultAsync(r => r.ShopId == shop && r.Id == id, ct).ConfigureAwait(false);
            if (row is null && await context.Set<PrintAttemptRecord>().AnyAsync(r => r.Id == id, ct).ConfigureAwait(false)) throw new FoundationException("PrintConflict");
            return row is null ? null : JsonSerializer.Deserialize<RetailCore.Application.Receipts.PrintAttempt>(row.DocumentJson);
        }
        public async Task<Guid> PrintShopAsync(Guid attemptId, CancellationToken ct) => (await context.Set<PrintAttemptRecord>().Where(r => r.Id == attemptId).Select(r => r.ShopId).SingleAsync(ct).ConfigureAwait(false)).Value;
        public void AddPrintAttempt(ShopId shop, RetailCore.Application.Receipts.PrintAttempt attempt) =>
            context.Add(new PrintAttemptRecord(shop, attempt.Id, attempt.SaleId, JsonSerializer.Serialize(attempt)));
        public void AddPrintOutcome(RetailCore.Application.Receipts.PrintOutcome outcome) =>
            context.Add(new PrintOutcomeRecord(outcome.AttemptId, JsonSerializer.Serialize(outcome)));
        public async Task<IReadOnlyList<RetailCore.Domain.Sales.Refund>> RefundsAsync(ShopId shop,SaleId original,CancellationToken ct)=>
            await context.Refunds.IgnoreQueryFilters().Include(r=>r.Lines).Where(r=>r.ShopId==shop && r.OriginalSaleId==original && r.CompletedAt!=null).ToListAsync(ct).ConfigureAwait(false);
        public void Add(RetailCore.Domain.Sales.Refund refund)=>context.Refunds.Add(refund);
        public void AddReturn(ShopId shop,RetailCore.Application.Returns.ReturnResult result)=>context.Add(new ReturnRecord(shop,result.Id,result.OriginalSaleId,JsonSerializer.Serialize(result)));
        public async Task<RetailCore.Application.Returns.ReturnResult?> ReturnAsync(ShopId shop,Guid id,CancellationToken ct)
        {
            var row=await context.Set<ReturnRecord>().SingleOrDefaultAsync(r=>r.ShopId==shop && r.Id==id,ct).ConfigureAwait(false);
            return row is null?null:JsonSerializer.Deserialize<RetailCore.Application.Returns.ReturnResult>(row.ResultJson);
        }
        public async Task<IReadOnlyList<RetailCore.Application.Returns.ReturnResult>> ReturnsAsync(ShopId shop,Guid original,CancellationToken ct)
        {
            var rows=await context.Set<ReturnRecord>().Where(r=>r.ShopId==shop && r.OriginalSaleId==new SaleId(original)).ToListAsync(ct).ConfigureAwait(false);
            return rows.Select(r=>JsonSerializer.Deserialize<RetailCore.Application.Returns.ReturnResult>(r.ResultJson)!).OrderByDescending(r=>r.Document.CompletedAt).ToArray();
        }
        public async Task<IReadOnlyList<RetailCore.Application.Receipts.PrintHistory>> ReturnPrintsAsync(ShopId shop,Guid id,CancellationToken ct)
        {
            var rows=await context.Set<ReturnPrintRecord>().Where(r=>r.ShopId==shop && r.DocumentId==id).ToListAsync(ct).ConfigureAwait(false);
            var ids=rows.Select(r=>r.Id).ToArray();var outcomes=await context.Set<ReturnPrintOutcomeRecord>().Where(r=>ids.Contains(r.Id)).ToListAsync(ct).ConfigureAwait(false);
            return rows.Select(r=>new RetailCore.Application.Receipts.PrintHistory(JsonSerializer.Deserialize<RetailCore.Application.Receipts.PrintAttempt>(r.DocumentJson)!,
                outcomes.Where(o=>o.Id==r.Id).Select(o=>JsonSerializer.Deserialize<RetailCore.Application.Receipts.PrintOutcome>(o.DocumentJson)).SingleOrDefault())).ToArray();
        }
        public void AddReturnPrint(ShopId shop,RetailCore.Application.Receipts.PrintAttempt attempt)=>context.Add(new ReturnPrintRecord(shop,attempt.Id,attempt.SaleId,JsonSerializer.Serialize(attempt)));
        public void AddReturnPrintOutcome(RetailCore.Application.Receipts.PrintOutcome outcome)=>context.Add(new ReturnPrintOutcomeRecord(outcome.AttemptId,JsonSerializer.Serialize(outcome)));
        public async Task<RetailCore.Application.Receipts.PrintAttempt?> ReturnPrintAsync(ShopId shop,Guid id,CancellationToken ct)
        {
            var row=await context.Set<ReturnPrintRecord>().SingleOrDefaultAsync(r=>r.Id==id,ct).ConfigureAwait(false);
            if(row is not null && row.ShopId!=shop)throw new FoundationException("PrintConflict");
            return row is null?null:JsonSerializer.Deserialize<RetailCore.Application.Receipts.PrintAttempt>(row.DocumentJson);
        }
        public void Add(Shop shop) => context.Shops.Add(shop);
        public void Add(Terminal terminal) => context.Terminals.Add(terminal);
        public void Add(User user) => context.Users.Add(user);
        public void Audit(AuditEntry entry) => context.AuditEntries.Add(entry);
    }
}
