using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RetailCore.Domain.Inventory;
using RetailCore.Domain.Security;
using RetailCore.Domain.Shifts;

namespace RetailCore.Infrastructure.Persistence.Configurations;

internal sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("StockMovements");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id)
            .HasConversion<ValueConverters.StockMovementIdConverter>()
            .ValueGeneratedNever();

        builder.Property(m => m.ShopId).HasConversion<ValueConverters.ShopIdConverter>().IsRequired();
        builder.Property(m => m.ProductId).HasConversion<ValueConverters.ProductIdConverter>().IsRequired();
        builder.Property(m => m.RecordedBy).HasConversion<ValueConverters.UserIdConverter>();

        builder.MapQuantity(m => m.Quantity, "Quantity");

        // Absent for movements the system posts from a sale, where the cost already lives on
        // the sale line (CAT-05). Present for goods received, where it is what stock is
        // valued at. INV-04.
        builder.MapOptionalMoney(m => m.UnitCost, "UnitCost");

        builder.Property(m => m.Direction).HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.Property(m => m.Kind).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(m => m.Reason).HasMaxLength(500);
        builder.Property(m => m.SourceType).HasMaxLength(50);

        builder.Ignore(m => m.SignedQuantity);

        // INV-02: stock on hand is a sum over this index, so it has to cover the lookup.
        builder.HasIndex(m => new { m.ShopId, m.ProductId, m.OccurredAt });

        // Tracing a movement back to the document that caused it.
        builder.HasIndex(m => new { m.SourceType, m.SourceId });
    }
}

internal sealed class ShiftConfiguration : IEntityTypeConfiguration<Shift>
{
    public void Configure(EntityTypeBuilder<Shift> builder)
    {
        builder.ToTable("Shifts");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
            .HasConversion<ValueConverters.ShiftIdConverter>()
            .ValueGeneratedNever();

        builder.Property(s => s.ShopId).HasConversion<ValueConverters.ShopIdConverter>().IsRequired();
        builder.Property(s => s.TerminalId).HasConversion<ValueConverters.TerminalIdConverter>().IsRequired();
        builder.Property(s => s.OpenedBy).HasConversion<ValueConverters.UserIdConverter>().IsRequired();
        builder.Property(s => s.ClosedBy).HasConversion<ValueConverters.UserIdConverter>();

        builder.Property(s => s.CurrencyCode)
            .HasMaxLength(MoneyMapping.CurrencyCodeLength)
            .IsFixedLength()
            .IsRequired();

        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(s => s.ClosingNotes).HasMaxLength(1000);

        builder.MapMoney(s => s.OpeningFloat, "OpeningFloat");

        // CSH-04. Absent until the drawer is counted.
        builder.MapOptionalMoney(s => s.CountedCash, "CountedCash");
        builder.MapOptionalMoney(s => s.ExpectedCash, "ExpectedCash");
        builder.MapOptionalMoney(s => s.Variance, "Variance");

        builder.HasMany(s => s.CashMovements)
            .WithOne()
            .HasForeignKey(m => m.ShiftId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(s => s.CashMovements)
            .HasField("_cashMovements")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(s => s.Currency);
        builder.Ignore(s => s.IsOpen);
        builder.Ignore(s => s.NetCashMovements);
        builder.Ignore(s => s.DomainEvents);

        builder.HasIndex(s => s.ShopId);

        // CSH-02: one open shift per terminal, enforced by the database rather than by a
        // check in code, because two sessions racing to open a shift is exactly the case an
        // in-memory check misses.
        //
        // Filtered to open shifts only, so a terminal can accumulate as many closed shifts
        // as it likes. Both SQLite and PostgreSQL support partial indexes and both accept
        // double-quoted identifiers, so one filter expression serves both providers.
        builder.HasIndex(s => s.TerminalId)
            .IsUnique()
            .HasFilter("\"Status\" = 'Open'")
            .HasDatabaseName("IX_Shifts_OneOpenPerTerminal");
    }
}

internal sealed class CashMovementConfiguration : IEntityTypeConfiguration<CashMovement>
{
    public void Configure(EntityTypeBuilder<CashMovement> builder)
    {
        builder.ToTable("CashMovements");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id)
            .HasConversion<ValueConverters.CashMovementIdConverter>()
            .ValueGeneratedNever();

        builder.Property(m => m.ShiftId).HasConversion<ValueConverters.ShiftIdConverter>().IsRequired();
        builder.Property(m => m.ShopId).HasConversion<ValueConverters.ShopIdConverter>().IsRequired();
        builder.Property(m => m.RecordedBy).HasConversion<ValueConverters.UserIdConverter>().IsRequired();
        builder.Property(m => m.Kind).HasConversion<string>().HasMaxLength(20).IsRequired();

        // CSH-05 and CSH-06: an unexplained cash movement is the gap that makes a Z report
        // variance impossible to investigate the next day.
        builder.Property(m => m.Reason).HasMaxLength(500).IsRequired();

        builder.MapMoney(m => m.Amount, "Amount");

        builder.Ignore(m => m.SignedAmount);

        builder.HasIndex(m => m.ShiftId);
    }
}

internal sealed class UserConfiguration : IEntityTypeConfiguration<Domain.Security.User>
{
    public void Configure(EntityTypeBuilder<Domain.Security.User> builder)
    {
        builder.ToTable("Users");

        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id)
            .HasConversion<ValueConverters.UserIdConverter>()
            .ValueGeneratedNever();

        builder.Property(u => u.ShopId).HasConversion<ValueConverters.ShopIdConverter>().IsRequired();
        builder.Property(u => u.Language).HasMaxLength(2).IsRequired();

        builder.Property(u => u.DisplayName).HasMaxLength(100).IsRequired();
        builder.Property(u => u.Role).HasConversion<string>().HasMaxLength(20).IsRequired();

        // SEC-02: an Argon2id encoded hash, which carries its own salt and parameters.
        builder.Property(u => u.PinHash).HasMaxLength(256).IsRequired();

        builder.Ignore(u => u.DomainEvents);

        builder.HasIndex(u => u.ShopId);
    }
}

internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("AuditEntries");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id)
            .HasConversion<ValueConverters.AuditEntryIdConverter>()
            .ValueGeneratedNever();

        builder.Property(a => a.ShopId).HasConversion<ValueConverters.ShopIdConverter>().IsRequired();
        builder.Property(a => a.PerformedBy).HasConversion<ValueConverters.UserIdConverter>();
        builder.Property(a => a.AuthorisedBy).HasConversion<ValueConverters.UserIdConverter>();
        builder.Property(a => a.TerminalId).HasConversion<ValueConverters.TerminalIdConverter>();

        builder.Property(a => a.Action).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(a => a.EntityType).HasMaxLength(50).IsRequired();
        builder.Property(a => a.Summary).HasMaxLength(1000);
        builder.Property(a => a.DetailsJson);

        // BO-05: the audit viewer reads by shop and time.
        builder.HasIndex(a => new { a.ShopId, a.OccurredAt });
        builder.HasIndex(a => new { a.EntityType, a.EntityId });
    }
}

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("Outbox");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();

        builder.Property(m => m.ShopId).HasConversion<ValueConverters.ShopIdConverter>().IsRequired();
        builder.Property(m => m.TerminalId).HasConversion<ValueConverters.TerminalIdConverter>().IsRequired();
        builder.Property(m => m.MessageType).HasMaxLength(200).IsRequired();
        builder.Property(m => m.Payload).IsRequired();
        builder.Property(m => m.LastError).HasMaxLength(2000);

        builder.Ignore(m => m.IsPending);

        // SYN-04 and OPS-05: the pump reads the pending queue in order, and the till shows
        // the depth of it. Covering only undispatched rows keeps the index small as history
        // accumulates.
        builder.HasIndex(m => new { m.DispatchedAt, m.CreatedAt });
    }
}

internal sealed class ReceiptCounterConfiguration : IEntityTypeConfiguration<ReceiptCounter>
{
    public void Configure(EntityTypeBuilder<ReceiptCounter> builder)
    {
        builder.ToTable("ReceiptCounters");

        // SYN-03: one row per terminal, which is what lets an offline till number its own
        // receipts without asking anybody.
        builder.HasKey(c => c.TerminalId);
        builder.Property(c => c.TerminalId)
            .HasConversion<ValueConverters.TerminalIdConverter>()
            .ValueGeneratedNever();

        builder.Property(c => c.ShopId).HasConversion<ValueConverters.ShopIdConverter>().IsRequired();
        builder.Property(c => c.TerminalCode).HasMaxLength(10).IsRequired();

        // Guards against a lost update handing the same number to two sales. Incremented by
        // the aggregate rather than the provider, so it behaves identically on SQLite and
        // PostgreSQL.
        builder.Property(c => c.Version).IsConcurrencyToken();
    }
}
