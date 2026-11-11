using Microsoft.EntityFrameworkCore;
using RetailCore.Application.Abstractions;
using RetailCore.Infrastructure.Persistence;

namespace RetailCore.Infrastructure.Sqlite;

/// <summary>
/// The database on the till. SYN-01.
/// </summary>
/// <remarks>
/// <para>
/// The till's copy is authoritative until it syncs, so the durability settings matter more
/// here than they would on a server with a replica. PLAN.md rates data loss at the shop as a
/// severe risk, and <c>synchronous=FULL</c> with write-ahead logging is the mitigation: a
/// completed sale survives the power going out, which in a minimart it does.
/// </para>
/// <para>
/// The model comes from <see cref="RetailCoreDbContext"/> unchanged. Nothing SQLite-specific
/// belongs in the shared model, only in the migrations generated from this context.
/// </para>
/// </remarks>
public sealed class TillDbContext(
    DbContextOptions<TillDbContext> options,
    ITenantContext tenantContext,
    IDomainEventDispatcher? eventDispatcher = null)
    : RetailCoreDbContext(options, tenantContext, eventDispatcher)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Foundation.BillingBasketRecord>(b =>
        {
            b.ToTable("BillingBaskets");
            b.HasKey(p => p.UserId);
            b.Property(p => p.UserId).HasConversion(v => v.Value, v => new RetailCore.Domain.Common.UserId(v)).ValueGeneratedNever();
            b.Property(p => p.ShopId).HasConversion(v => v.Value, v => new RetailCore.Domain.Common.ShopId(v));
            b.Property(p => p.TerminalId).HasConversion(v => v.Value, v => new RetailCore.Domain.Common.TerminalId(v));
            b.Property(p => p.DocumentJson).IsRequired();
        });
        modelBuilder.Entity<Foundation.CommandRecord>(builder =>
        {
            builder.ToTable("LocalCommands");
            builder.HasKey(r => r.Id);
            builder.Property(r => r.Id).ValueGeneratedNever();
            builder.Property(r => r.Kind).HasMaxLength(100).IsRequired();
            builder.Property(r => r.Fingerprint).HasMaxLength(256).IsRequired();
            builder.Property(r => r.ResultJson).IsRequired();
        });
    }

    /// <summary>
    /// Applies the durability settings a till needs. Run once after opening the connection.
    /// </summary>
    public async Task ConfigureForTillAsync(CancellationToken cancellationToken = default)
    {
        // WAL lets a reader (the sync pump) run while the cashier is writing a sale.
        await Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken).ConfigureAwait(false);

        // FULL costs a few milliseconds per commit and buys the guarantee that a sale the
        // cashier saw completed is on the disk. That trade is not close.
        await Database.ExecuteSqlRawAsync("PRAGMA synchronous=FULL;", cancellationToken).ConfigureAwait(false);

        await Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=ON;", cancellationToken).ConfigureAwait(false);
    }
}
