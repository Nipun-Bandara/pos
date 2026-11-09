using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RetailCore.Domain.Sales;

namespace RetailCore.Infrastructure.Persistence.Configurations;

internal sealed class RefundConfiguration : IEntityTypeConfiguration<Refund>
{
    public void Configure(EntityTypeBuilder<Refund> builder)
    {
        builder.ToTable("Refunds");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id)
            .HasConversion<ValueConverters.RefundIdConverter>()
            .ValueGeneratedNever();

        builder.Property(r => r.OriginalSaleId).HasConversion<ValueConverters.SaleIdConverter>().IsRequired();
        builder.Property(r => r.ShopId).HasConversion<ValueConverters.ShopIdConverter>().IsRequired();
        builder.Property(r => r.TerminalId).HasConversion<ValueConverters.TerminalIdConverter>().IsRequired();
        builder.Property(r => r.ShiftId).HasConversion<ValueConverters.ShiftIdConverter>().IsRequired();
        builder.Property(r => r.CashierId).HasConversion<ValueConverters.UserIdConverter>().IsRequired();

        // RET-05: a refund always has an authoriser, so the column is not nullable.
        builder.Property(r => r.AuthorisedBy).HasConversion<ValueConverters.UserIdConverter>().IsRequired();

        builder.Property(r => r.CurrencyCode)
            .HasMaxLength(MoneyMapping.CurrencyCodeLength)
            .IsFixedLength()
            .IsRequired();

        builder.Property(r => r.Reason).HasMaxLength(500).IsRequired();
        builder.Property(r => r.Method).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.Property(r => r.ReceiptNumber)
            .HasConversion<ValueConverters.ReceiptNumberConverter>()
            .HasMaxLength(32);

        builder.MapMoney(r => r.Total, "Total");
        builder.MapMoney(r => r.TaxTotal, "Tax");

        builder.HasMany(r => r.Lines)
            .WithOne()
            .HasForeignKey(l => l.RefundId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(r => r.Lines).HasField("_lines").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(r => r.Currency);
        builder.Ignore(r => r.IsCompleted);
        builder.Ignore(r => r.DomainEvents);

        // RET-03 needs to find every refund against a sale, across separate visits.
        builder.HasIndex(r => r.OriginalSaleId);
        builder.HasIndex(r => r.ShopId);
        builder.HasIndex(r => r.ShiftId);
    }
}

internal sealed class RefundLineConfiguration : IEntityTypeConfiguration<RefundLine>
{
    public void Configure(EntityTypeBuilder<RefundLine> builder)
    {
        builder.ToTable("RefundLines");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id)
            .HasConversion<ValueConverters.RefundLineIdConverter>()
            .ValueGeneratedNever();

        builder.Property(l => l.RefundId).HasConversion<ValueConverters.RefundIdConverter>().IsRequired();
        builder.Property(l => l.ProductId).HasConversion<ValueConverters.ProductIdConverter>().IsRequired();
        builder.Property(l => l.StockProductId).HasConversion<ValueConverters.ProductIdConverter>();
        builder.Property(l => l.StockBaseUnit).HasConversion<string>().HasMaxLength(20);
        builder.Property(l => l.Disposition).HasConversion<string>().HasMaxLength(20);

        // RET-03: the line this reverses, so what is left to refund can be worked out.
        builder.Property(l => l.OriginalSaleLineId)
            .HasConversion<ValueConverters.SaleLineIdConverter>()
            .IsRequired();

        builder.Property(l => l.Sku).HasMaxLength(50).IsRequired();
        builder.Property(l => l.Description).HasMaxLength(200).IsRequired();

        builder.MapQuantity(l => l.Quantity, "Quantity");

        builder.MapMoney(l => l.Amount, "Amount");

        builder.ComplexProperty(l => l.TaxTreatment, tax =>
        {
            tax.Property(t => t.Code).HasColumnName("TaxCode").HasMaxLength(20);
            tax.Property(t => t.Category).HasColumnName("TaxCategory").HasConversion<string>().HasMaxLength(20);
            tax.Property(t => t.Rate).HasColumnName("TaxRate").HasPrecision(8, 4);
            tax.Ignore(t => t.IsTaxable);
        });

        builder.Ignore(l => l.TaxAmount);

        builder.HasIndex(l => l.RefundId);
        builder.HasIndex(l => l.OriginalSaleLineId);
    }
}
