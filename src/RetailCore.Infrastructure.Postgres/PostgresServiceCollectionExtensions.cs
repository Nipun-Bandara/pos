using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailCore.Application.Abstractions;
using RetailCore.Infrastructure.Persistence;

namespace RetailCore.Infrastructure.Postgres;

public static class PostgresServiceCollectionExtensions
{
    /// <summary>Registers the central PostgreSQL database and everything shared.</summary>
    public static IServiceCollection AddCentralDatabase(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<CentralDbContext>((sp, options) =>
        {
            var tenant = sp.GetRequiredService<ITenantContext>();

            options
                .UseNpgsql(
                    connectionString,
                    npgsql => npgsql.MigrationsAssembly(typeof(CentralDbContext).Assembly.FullName))
                .AddInterceptors(
                    new TenantConnectionInterceptor(tenant),
                    new TenantTransactionInterceptor(tenant));
        });

        services.AddScoped<RetailCoreDbContext>(sp => sp.GetRequiredService<CentralDbContext>());

        return services.AddRetailCorePersistence();
    }
}
