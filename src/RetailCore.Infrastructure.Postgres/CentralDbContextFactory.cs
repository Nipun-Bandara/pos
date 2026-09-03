using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using RetailCore.Application.Abstractions;
using RetailCore.Domain.Common;

namespace RetailCore.Infrastructure.Postgres;

/// <summary>Lets <c>dotnet ef</c> build the context without starting the application.</summary>
public sealed class CentralDbContextFactory : IDesignTimeDbContextFactory<CentralDbContext>
{
    public CentralDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CentralDbContext>()
            .UseNpgsql("Host=localhost;Database=retailcore_design;Username=postgres")
            .Options;

        return new CentralDbContext(options, new DesignTimeTenantContext());
    }

    /// <summary>No tenant at design time, so the generated schema does not depend on one.</summary>
    private sealed class DesignTimeTenantContext : ITenantContext
    {
        public ShopId? CurrentShopId => null;

        public bool IsFilteringSuspended => true;
    }
}
