using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RetailCore.Application.Abstractions;

namespace RetailCore.Infrastructure.Postgres;

/// <summary>
/// Shared logic for telling PostgreSQL which shop the caller is. TEN-02.
/// </summary>
/// <remarks>
/// Row level security policies read <c>app.shop_id</c>. If nothing sets it, the policies see
/// NULL, no comparison matches, and the caller sees no rows at all. That is the correct
/// failure mode, and it is also why this has to run on every path that reaches the database,
/// not just on writes.
/// </remarks>
internal static class TenantSetting
{
    /// <summary>
    /// Sets the shop for the current connection or transaction.
    /// </summary>
    /// <param name="isLocal">
    /// True for <c>SET LOCAL</c>, scoping the value to the open transaction. False for the
    /// session, which is what a read outside a transaction needs.
    /// </param>
    public static async Task ApplyAsync(
        DbConnection connection,
        DbTransaction? transaction,
        ITenantContext tenantContext,
        bool isLocal,
        CancellationToken cancellationToken)
    {
        if (tenantContext.CurrentShopId is not { } shopId)
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;

        // set_config takes the value as a parameter, so the identifier is never spliced into
        // SQL text. The third argument is the SET LOCAL flag.
        command.CommandText = "SELECT set_config('app.shop_id', $1, $2);";

        var value = command.CreateParameter();
        value.Value = shopId.Value.ToString("D", CultureInfo.InvariantCulture);
        command.Parameters.Add(value);

        var local = command.CreateParameter();
        local.Value = isLocal;
        command.Parameters.Add(local);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Sets the shop when a connection opens, so that reads outside a transaction are filtered.
/// </summary>
/// <remarks>
/// Session scoped rather than transaction scoped, because a plain query does not open a
/// transaction and would otherwise reach the policies with no shop set and read nothing.
/// <para>
/// Safe with connection pooling because Npgsql resets session state when a connection is
/// returned to the pool, and because this runs on every open. A pooled connection therefore
/// cannot carry one shop's identity into the next caller's work.
/// </para>
/// </remarks>
public sealed class TenantConnectionInterceptor(ITenantContext tenantContext) : DbConnectionInterceptor
{
    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await TenantSetting
            .ApplyAsync(connection, transaction: null, tenantContext, isLocal: false, cancellationToken)
            .ConfigureAwait(false);

        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken).ConfigureAwait(false);
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        TenantSetting
            .ApplyAsync(connection, transaction: null, tenantContext, isLocal: false, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        base.ConnectionOpened(connection, eventData);
    }
}

/// <summary>
/// Re-asserts the shop inside each transaction with <c>SET LOCAL</c>. TEN-02.
/// </summary>
/// <remarks>
/// PLAN.md Phase 1 calls for the tenant to be set inside the transaction, and this is that.
/// It is not redundant with the connection interceptor: a unit of work may begin after the
/// tenant has been established, and scoping the value to the transaction means it is
/// unwound with a rollback rather than outliving it.
/// </remarks>
public sealed class TenantTransactionInterceptor(ITenantContext tenantContext) : DbTransactionInterceptor
{
    public override async ValueTask<DbTransaction> TransactionStartedAsync(
        DbConnection connection,
        TransactionEndEventData eventData,
        DbTransaction result,
        CancellationToken cancellationToken = default)
    {
        await TenantSetting
            .ApplyAsync(connection, result, tenantContext, isLocal: true, cancellationToken)
            .ConfigureAwait(false);

        return await base.TransactionStartedAsync(connection, eventData, result, cancellationToken)
            .ConfigureAwait(false);
    }

    public override async ValueTask<DbTransaction> TransactionUsedAsync(
        DbConnection connection,
        TransactionEventData eventData,
        DbTransaction result,
        CancellationToken cancellationToken = default)
    {
        await TenantSetting
            .ApplyAsync(connection, result, tenantContext, isLocal: true, cancellationToken)
            .ConfigureAwait(false);

        return await base.TransactionUsedAsync(connection, eventData, result, cancellationToken)
            .ConfigureAwait(false);
    }
}
