using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailCore.Infrastructure.Persistence;

namespace RetailCore.Infrastructure.Sqlite;

public static class SqliteServiceCollectionExtensions
{
    /// <summary>Registers the till's SQLite database and everything shared.</summary>
    public static IServiceCollection AddTillDatabase(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<TillDbContext>(options => options.UseSqlite(
            connectionString,
            sqlite => sqlite.MigrationsAssembly(typeof(TillDbContext).Assembly.FullName)));

        services.AddScoped<RetailCoreDbContext>(sp => sp.GetRequiredService<TillDbContext>());

        return services.AddRetailCorePersistence();
    }
}
