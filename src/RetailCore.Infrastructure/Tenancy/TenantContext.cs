using RetailCore.Application.Abstractions;
using RetailCore.Domain.Common;

namespace RetailCore.Infrastructure.Tenancy;

/// <summary>
/// Holds the shop for the current scope. TEN-01.
/// </summary>
/// <remarks>
/// Registered as scoped: one per request on the server, one per unit of work on the till.
/// The till sets it once at startup because a till serves one shop for its whole life.
/// </remarks>
public sealed class TenantContext : ITenantContextAccessor
{
    private int _suspensions;

    public ShopId? CurrentShopId { get; private set; }

    public bool IsFilteringSuspended => _suspensions > 0;

    public void SetShop(ShopId shopId) => CurrentShopId = shopId;

    /// <summary>
    /// Suspends tenant filtering until the returned scope is disposed. TEN-04 relies on this
    /// to prove that row level security still holds when the application-level filter is off.
    /// </summary>
    public IDisposable SuspendFiltering()
    {
        _suspensions++;
        return new Suspension(this);
    }

    private sealed class Suspension(TenantContext owner) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            owner._suspensions--;
        }
    }
}
