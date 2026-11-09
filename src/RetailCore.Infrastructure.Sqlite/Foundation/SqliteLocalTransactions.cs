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
        public void Add(Shop shop) => context.Shops.Add(shop);
        public void Add(Terminal terminal) => context.Terminals.Add(terminal);
        public void Add(User user) => context.Users.Add(user);
        public void Audit(AuditEntry entry) => context.AuditEntries.Add(entry);
    }
}
