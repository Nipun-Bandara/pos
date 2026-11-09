using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RetailCore.Domain.Tenancy;

namespace RetailCore.Infrastructure.Persistence.Configurations;

internal sealed class ShopConfiguration : IEntityTypeConfiguration<Shop>
{
    public void Configure(EntityTypeBuilder<Shop> builder)
    {
        builder.ToTable("Shops");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
            .HasConversion<ValueConverters.ShopIdConverter>()
            .ValueGeneratedNever();

        builder.Property(s => s.ReceiptFooter).HasMaxLength(1000);
        builder.Property(s => s.PrinterName).HasMaxLength(200);

        builder.Property(s => s.Name).HasMaxLength(200).IsRequired();
        builder.Property(s => s.LegalName).HasMaxLength(200).IsRequired();

        // CMP-05: printed on every receipt where the shop is registered.
        builder.Property(s => s.TaxRegistrationNumber).HasMaxLength(50);
        builder.Property(s => s.AddressLine1).HasMaxLength(200);
        builder.Property(s => s.AddressLine2).HasMaxLength(200);
        builder.Property(s => s.Telephone).HasMaxLength(50);
        builder.Property(s => s.TimeZoneId).HasMaxLength(100).IsRequired();

        builder.ComplexProperty(s => s.Currency, currency =>
            currency.Property(c => c.Code)
                .HasColumnName("CurrencyCode")
                .HasMaxLength(MoneyMapping.CurrencyCodeLength)
                .IsFixedLength());

        builder.Ignore(s => s.DomainEvents);

        // Shops are the tenant list itself, so this table is not tenant filtered.
    }
}

internal sealed class TerminalConfiguration : IEntityTypeConfiguration<Terminal>
{
    public void Configure(EntityTypeBuilder<Terminal> builder)
    {
        builder.ToTable("Terminals");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id)
            .HasConversion<ValueConverters.TerminalIdConverter>()
            .ValueGeneratedNever();

        builder.Property(t => t.ShopId).HasConversion<ValueConverters.ShopIdConverter>().IsRequired();
        builder.Property(t => t.Code).HasMaxLength(10).IsRequired();
        builder.Property(t => t.Name).HasMaxLength(100).IsRequired();
        builder.Property(t => t.InstalledVersion).HasMaxLength(50);

        // The code prefixes receipt numbers, so it has to be unique within the shop or two
        // tills would print the same receipt number. SYN-03.
        builder.HasIndex(t => new { t.ShopId, t.Code }).IsUnique();

        builder.Ignore(t => t.DomainEvents);
    }
}
