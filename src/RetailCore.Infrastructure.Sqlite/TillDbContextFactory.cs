using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using RetailCore.Application.Abstractions;
using RetailCore.Domain.Common;

namespace RetailCore.Infrastructure.Sqlite;

/// <summary>
/// Lets <c>dotnet ef</c> build the context without starting the application.
/// </summary>
/// <remarks>
/// Only used by the migrations tooling. The connection string here is never opened: the
/// designer needs a provider to generate SQL against, not a database to talk to.
/// </remarks>
public sealed class TillDbContextFactory : IDesignTimeDbContextFactory<TillDbContext>
{
    public TillDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TillDbContext>()
            .UseSqlite("Data Source=design-time.db")
            .Options;

        return new TillDbContext(options, new DesignTimeTenantContext());
    }

    /// <summary>
    /// No tenant at design time, so query filters compile to "unfiltered" and the generated
    /// schema does not depend on which shop happens to be in scope.
    /// </summary>
    private sealed class DesignTimeTenantContext : ITenantContext
    {
        public ShopId? CurrentShopId => null;

        public bool IsFilteringSuspended => true;
    }
}
