using RetailCore.Domain.Common;

namespace RetailCore.Application.Abstractions;

/// <summary>
/// Which shop the current unit of work belongs to. TEN-01.
/// </summary>
/// <remarks>
/// Everything that reads or writes goes through this. On the server it is set from the
/// authenticated principal; on a till it is fixed at startup because a till only ever
/// serves one shop. The tenant interceptor issues <c>SET LOCAL app.shop_id</c> from it so
/// that PostgreSQL row level security (TEN-02) sees the same value the query filters do.
/// </remarks>
public interface ITenantContext
{
    /// <summary>The shop in scope, or null when running without a tenant.</summary>
    ShopId? CurrentShopId { get; }

    /// <summary>
    /// True when tenant filtering is deliberately suspended, which only provisioning and
    /// migrations may do. TEN-04 asserts that suspending it in application code still does
    /// not leak, because row level security is the primary control and this is defence in
    /// depth.
    /// </summary>
    bool IsFilteringSuspended { get; }
}

/// <summary>Sets the shop for the current scope.</summary>
public interface ITenantContextAccessor : ITenantContext
{
    void SetShop(ShopId shopId);

    /// <summary>
    /// Suspends tenant filtering until the returned scope is disposed. For provisioning and
    /// for the tests that prove row level security holds without it.
    /// </summary>
    IDisposable SuspendFiltering();
}
