using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailCore.Application.Foundation;
using RetailCore.Infrastructure.Persistence;

namespace RetailCore.Infrastructure.Sqlite.Foundation;

public static class LocalRuntime
{
    public static string DefaultDatabasePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RetailCore", "retailcore.db");

    public static IServiceCollection AddLocalFoundation(this IServiceCollection services, string databasePath)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        var path = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var connection = new SqliteConnectionStringBuilder
        {
            DataSource = path, ForeignKeys = true, DefaultTimeout = 5,
        }.ToString();
        services.AddRetailCorePersistence();
        services.AddDbContext<TillDbContext>(options => options.UseSqlite(connection)
            .AddInterceptors(new SqliteDurabilityInterceptor()));
        services.AddScoped<RetailCoreDbContext>(sp => sp.GetRequiredService<TillDbContext>());
        services.AddSingleton<ILocalTransactions, SqliteLocalTransactions>();
        services.AddSingleton<IPinHasher, Argon2PinHasher>();
        services.AddSingleton<LocalSession>();
        services.AddSingleton<FoundationService>();
        services.AddSingleton<RetailCore.Application.Reporting.CashService>();
        services.AddSingleton<RetailCore.Application.Reporting.ReportService>();
        services.AddSingleton<RetailCore.Application.Receipts.ReceiptService>();
        services.AddSingleton<RetailCore.Application.Returns.ReturnService>();
        services.AddSingleton<RetailCore.Application.Checkout.BillingService>();
        services.AddSingleton<RetailCore.Application.Inventory.StockService>();
        services.AddSingleton<RetailCore.Application.Catalogue.CatalogueService>();
        return services;
    }
}
