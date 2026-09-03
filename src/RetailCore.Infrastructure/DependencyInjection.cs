using Microsoft.Extensions.DependencyInjection;
using RetailCore.Application.Abstractions;
using RetailCore.Application.Inventory;
using RetailCore.Application.Security;
using RetailCore.Domain.Sales;
using RetailCore.Infrastructure.Persistence;
using RetailCore.Infrastructure.Tenancy;

namespace RetailCore.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers everything the persistence layer needs, minus the provider itself.
    /// </summary>
    /// <remarks>
    /// The provider is chosen by the host: SQLite on the till, PostgreSQL on the server.
    /// Everything below is shared, which is what keeps the two in step.
    /// </remarks>
    public static IServiceCollection AddRetailCorePersistence(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.AddScoped<ITenantContextAccessor>(sp => sp.GetRequiredService<TenantContext>());

        services.AddSingleton<IClock>(SystemClock.Instance);

        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IReceiptNumberAllocator, ReceiptNumberAllocator>();

        services.AddScoped<ISaleRepository, SaleRepository>();
        services.AddScoped<IRefundRepository, RefundRepository>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<ITaxRateRepository, TaxRateRepository>();
        services.AddScoped<IShiftRepository, ShiftRepository>();
        services.AddScoped<IStockLedger, StockLedger>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IAuditLog, AuditLog>();
        services.AddScoped<IShopRepository, ShopRepository>();

        return services.AddRetailCoreEventHandlers();
    }

    /// <summary>
    /// Registers the handlers that run inside a unit of work.
    /// </summary>
    /// <remarks>
    /// Listed explicitly rather than discovered by scanning. There are few of them, they all
    /// write to the database inside somebody else's transaction, and knowing exactly what runs
    /// when a sale completes is worth more than the convenience of registration by convention.
    /// </remarks>
    public static IServiceCollection AddRetailCoreEventHandlers(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // INV-03 and RET-06: the stock ledger is written from events, never directly.
        services.AddScoped<IDomainEventHandler<SaleCompleted>, PostStockForCompletedSale>();
        services.AddScoped<IDomainEventHandler<SaleVoided>, PostStockForVoidedSale>();
        services.AddScoped<IDomainEventHandler<RefundIssued>, PostStockForRefund>();

        // SEC-06: privileged actions are recorded in the same transaction that performs them.
        services.AddScoped<IDomainEventHandler<SaleVoided>, AuditSaleVoided>();
        services.AddScoped<IDomainEventHandler<SaleLineVoided>, AuditSaleLineVoided>();
        services.AddScoped<IDomainEventHandler<DiscountApplied>, AuditDiscountApplied>();
        services.AddScoped<IDomainEventHandler<RefundIssued>, AuditRefundIssued>();

        return services;
    }
}
