using RetailCore.Application.Abstractions;
using RetailCore.Domain.Common;

namespace RetailCore.Integration.Tests;

/// <summary>A tenant context the tests can point at whichever shop they are exercising.</summary>
internal sealed class TestTenantContext : ITenantContextAccessor
{
    private int _suspensions;

    public ShopId? CurrentShopId { get; private set; }

    public bool IsFilteringSuspended => _suspensions > 0;

    public void SetShop(ShopId shopId) => CurrentShopId = shopId;

    public void ClearShop() => CurrentShopId = null;

    public IDisposable SuspendFiltering()
    {
        _suspensions++;
        return new Suspension(this);
    }

    private sealed class Suspension(TestTenantContext owner) : IDisposable
    {
        public void Dispose() => owner._suspensions--;
    }
}
