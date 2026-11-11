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
        modelBuilder.Entity<Foundation.ReturnRecord>(b=>{
            b.ToTable("ReturnDocuments");b.HasKey(r=>r.Id);b.Property(r=>r.Id).ValueGeneratedNever();b.Property(r=>r.ResultJson).IsRequired();
            b.HasIndex(r=>new {r.ShopId,r.OriginalSaleId});
            b.HasOne<RetailCore.Domain.Sales.Sale>().WithMany().HasForeignKey(r=>r.OriginalSaleId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<Foundation.ReturnPrintRecord>(b=>{
            b.ToTable("ReturnPrintAttempts");b.HasKey(r=>r.Id);b.Property(r=>r.Id).ValueGeneratedNever();b.Property(r=>r.DocumentJson).IsRequired();
            b.HasIndex(r=>new {r.ShopId,r.DocumentId});b.HasOne<Foundation.ReturnRecord>().WithMany().HasForeignKey(r=>r.DocumentId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<Foundation.ReturnPrintOutcomeRecord>(b=>{
            b.ToTable("ReturnPrintOutcomes");b.HasKey(r=>r.Id);b.Property(r=>r.Id).ValueGeneratedNever();b.Property(r=>r.DocumentJson).IsRequired();
            b.HasOne<Foundation.ReturnPrintRecord>().WithOne().HasForeignKey<Foundation.ReturnPrintOutcomeRecord>(r=>r.Id).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<Foundation.ReceiptRecord>(b =>
        {
            b.ToTable("ReceiptDocuments"); b.HasKey(r => r.SaleId);
            b.Property(r => r.SaleId).ValueGeneratedNever(); b.Property(r => r.DocumentJson).IsRequired();
            b.HasIndex(r => r.ShopId);
            b.HasOne<RetailCore.Domain.Sales.Sale>().WithOne().HasForeignKey<Foundation.ReceiptRecord>(r => r.SaleId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<Foundation.PrintAttemptRecord>(b =>
        {
            b.ToTable("ReceiptPrintAttempts"); b.HasKey(r => r.Id);
            b.Property(r => r.Id).ValueGeneratedNever(); b.Property(r => r.DocumentJson).IsRequired();
            b.HasIndex(r => new { r.ShopId, r.SaleId });
            b.HasOne<Foundation.ReceiptRecord>().WithMany().HasForeignKey(r => r.SaleId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<Foundation.PrintOutcomeRecord>(b =>
        {
            b.ToTable("ReceiptPrintOutcomes"); b.HasKey(r => r.AttemptId);
            b.Property(r => r.AttemptId).ValueGeneratedNever(); b.Property(r => r.DocumentJson).IsRequired();
            b.HasOne<Foundation.PrintAttemptRecord>().WithOne().HasForeignKey<Foundation.PrintOutcomeRecord>(r => r.AttemptId).OnDelete(DeleteBehavior.Restrict);
        });
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
