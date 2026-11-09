using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using RetailCore.Application.Abstractions;
using RetailCore.Domain.Catalogue;
using RetailCore.Domain.Common;
using RetailCore.Domain.Inventory;
using RetailCore.Domain.Sales;
using RetailCore.Domain.Security;
using RetailCore.Domain.Shifts;
using RetailCore.Domain.Tax;
using RetailCore.Domain.Tenancy;

namespace RetailCore.Infrastructure.Persistence;

/// <summary>
/// The shared model for both providers.
/// </summary>
/// <remarks>
/// <para>
/// One context, two providers. The till runs it on SQLite and the server on PostgreSQL, and
/// PLAN.md Phase 1 exits when a sale round-trips through both with identical totals. Keeping
/// a single model is what makes that testable; two models drift the first time somebody
/// fixes a column on one side only.
/// </para>
/// <para>
/// Provider-specific concerns live in the derived contexts and their own migration
/// assemblies, so nothing here can quietly depend on PostgreSQL.
/// </para>
/// </remarks>
public class RetailCoreDbContext(
    DbContextOptions options,
    ITenantContext tenantContext,
    IDomainEventDispatcher? eventDispatcher = null) : DbContext(options)
{
    private readonly ITenantContext _tenantContext = tenantContext;

    /// <summary>
    /// The shop every tenant-scoped query is restricted to. TEN-03.
    /// </summary>
    /// <remarks>
    /// EF query filters are defence in depth, not the control. PostgreSQL row level security
    /// (TEN-02) is the control, because it holds even when a query is written by hand or a
    /// filter is forgotten. TEN-04 exists to prove exactly that.
    /// </remarks>
    public ShopId TenantShopId => _tenantContext.CurrentShopId ?? default;

    /// <summary>False when filtering is suspended for provisioning or a tenancy test.</summary>
    public bool IsTenantFilterEnabled =>
        !_tenantContext.IsFilteringSuspended && _tenantContext.CurrentShopId is not null;

    public DbSet<Shop> Shops => Set<Shop>();

    public DbSet<Terminal> Terminals => Set<Terminal>();

    public DbSet<User> Users => Set<User>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<TaxRate> TaxRates => Set<TaxRate>();

    public DbSet<Sale> Sales => Set<Sale>();

    public DbSet<Refund> Refunds => Set<Refund>();

    public DbSet<StockMovement> StockMovements => Set<StockMovement>();

    public DbSet<Shift> Shifts => Set<Shift>();

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

    public DbSet<ReceiptCounter> ReceiptCounters => Set<ReceiptCounter>();

    /// <summary>
    /// Registers the identifier conversions once for the whole model.
    /// </summary>
    /// <remarks>
    /// Done by convention rather than property by property so that a new entity carrying a
    /// <see cref="ShopId"/> cannot be added and left unmapped, and so that nullable
    /// identifiers behave the same as required ones without a second registration.
    /// </remarks>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        configurationBuilder.Properties<ShopId>().HaveConversion<ValueConverters.ShopIdConverter>();
        configurationBuilder.Properties<TerminalId>().HaveConversion<ValueConverters.TerminalIdConverter>();
        configurationBuilder.Properties<UserId>().HaveConversion<ValueConverters.UserIdConverter>();
        configurationBuilder.Properties<ProductId>().HaveConversion<ValueConverters.ProductIdConverter>();
        configurationBuilder.Properties<CategoryId>().HaveConversion<ValueConverters.CategoryIdConverter>();
        configurationBuilder.Properties<TaxRateId>().HaveConversion<ValueConverters.TaxRateIdConverter>();
        configurationBuilder.Properties<SaleId>().HaveConversion<ValueConverters.SaleIdConverter>();
        configurationBuilder.Properties<SaleLineId>().HaveConversion<ValueConverters.SaleLineIdConverter>();
        configurationBuilder.Properties<PaymentId>().HaveConversion<ValueConverters.PaymentIdConverter>();
        configurationBuilder.Properties<RefundId>().HaveConversion<ValueConverters.RefundIdConverter>();
        configurationBuilder.Properties<RefundLineId>().HaveConversion<ValueConverters.RefundLineIdConverter>();
        configurationBuilder.Properties<ShiftId>().HaveConversion<ValueConverters.ShiftIdConverter>();
        configurationBuilder.Properties<CashMovementId>()
            .HaveConversion<ValueConverters.CashMovementIdConverter>();
        configurationBuilder.Properties<StockMovementId>()
            .HaveConversion<ValueConverters.StockMovementIdConverter>();
        configurationBuilder.Properties<AuditEntryId>()
            .HaveConversion<ValueConverters.AuditEntryIdConverter>();

        base.ConfigureConventions(configurationBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RetailCoreDbContext).Assembly);

        ApplyTenantFilters(modelBuilder);

        base.OnModelCreating(modelBuilder);
    }

    /// <summary>
    /// Restricts every tenant-scoped table to the current shop. TEN-03.
    /// </summary>
    /// <remarks>
    /// Applied by walking the model rather than repeated in each configuration file, so a new
    /// table cannot be added and quietly left unfiltered. The filter is written as
    /// "filtering is off, or the shop matches" so that provisioning and the tenancy tests can
    /// suspend it deliberately, and so the expression stays translatable to SQL.
    /// </remarks>
    private void ApplyTenantFilters(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (entityType.IsOwned() || !typeof(IBelongsToShop).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var shopId = Expression.Property(parameter, nameof(IBelongsToShop.ShopId));

            var filterEnabled = Expression.Property(
                Expression.Constant(this),
                nameof(IsTenantFilterEnabled));

            var currentShop = Expression.Property(
                Expression.Constant(this),
                nameof(TenantShopId));

            var body = Expression.OrElse(
                Expression.Not(filterEnabled),
                Expression.Equal(shopId, currentShop));

            modelBuilder.Entity(entityType.ClrType)
                .HasQueryFilter(Expression.Lambda(body, parameter));
        }
    }

    /// <summary>
    /// Saves, dispatching the domain events raised along the way inside the same transaction.
    /// </summary>
    /// <remarks>
    /// Handlers run before the write, so anything they add (stock movements for INV-03, audit
    /// entries for SEC-06, outbox rows for SYN-04) is part of the same SaveChanges and the
    /// same transaction. A handler that fails takes the sale down with it, which is the
    /// correct outcome: a sale whose stock movements did not post is worse than no sale.
    /// <para>
    /// The loop exists because a handler may raise further events. It is bounded so that a
    /// pair of handlers raising each other's events fails loudly rather than hanging a till.
    /// </para>
    /// </remarks>
    private bool _writeFailed;

    public override int SaveChanges(bool acceptAllChangesOnSuccess) =>
        SaveChangesAsync(acceptAllChangesOnSuccess).GetAwaiter().GetResult();

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        SaveChangesAsync(true, cancellationToken);

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        if (_writeFailed)
        {
            throw new InvalidOperationException("Discard the failed unit of work and retry using a fresh context.");
        }

        try
        {
            for (var round = 0; round < 8; round++)
            {
                var pending = ChangeTracker.Entries().Select(e => e.Entity).OfType<IHasDomainEvents>()
                    .Any(root => root.DomainEvents.Count > 0);
                if (!pending)
                {
                    return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken).ConfigureAwait(false);
                }

                if (eventDispatcher is null)
                {
                    throw new InvalidOperationException("Pending domain events require a configured dispatcher.");
                }

                await eventDispatcher.DispatchAsync(DrainDomainEvents(), cancellationToken).ConfigureAwait(false);
            }

            throw new InvalidOperationException("Domain event cascade limit exceeded.");
        }
        catch
        {
            _writeFailed = true;
            throw;
        }
    }

    private List<IDomainEvent> DrainDomainEvents()
    {
        var roots = ChangeTracker
            .Entries()
            .Select(entry => entry.Entity)
            .OfType<IHasDomainEvents>()
            .Where(root => root.DomainEvents.Count > 0)
            .ToList();

        var events = roots.SelectMany(root => root.DomainEvents).ToList();

        foreach (var root in roots)
        {
            root.ClearDomainEvents();
        }

        return events;
    }
}
