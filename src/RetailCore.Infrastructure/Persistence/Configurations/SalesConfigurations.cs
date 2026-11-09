using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RetailCore.Domain.Sales;

namespace RetailCore.Infrastructure.Persistence.Configurations;

internal sealed class SaleConfiguration : IEntityTypeConfiguration<Sale>
{
    public void Configure(EntityTypeBuilder<Sale> builder)
    {
        builder.ToTable("Sales");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
            .HasConversion<ValueConverters.SaleIdConverter>()
            .ValueGeneratedNever();

        builder.Property(s => s.ShopId).HasConversion<ValueConverters.ShopIdConverter>().IsRequired();
        builder.Property(s => s.TerminalId).HasConversion<ValueConverters.TerminalIdConverter>().IsRequired();
        builder.Property(s => s.ShiftId).HasConversion<ValueConverters.ShiftIdConverter>().IsRequired();
        builder.Property(s => s.CashierId).HasConversion<ValueConverters.UserIdConverter>().IsRequired();
        builder.Property(s => s.VoidedBy).HasConversion<ValueConverters.UserIdConverter>();

        builder.Property(s => s.CurrencyCode)
            .HasMaxLength(MoneyMapping.CurrencyCodeLength)
            .IsFixedLength()
            .IsRequired();

        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        // SYN-03. Nullable until the sale completes, so an abandoned basket burns no number.
        builder.Property(s => s.ReceiptNumber)
            .HasConversion<ValueConverters.ReceiptNumberConverter>()
            .HasMaxLength(32);

        builder.Property(s => s.VoidReason).HasMaxLength(500);
        builder.Property(s => s.CustomerName).HasMaxLength(200);
        builder.Property(s => s.CustomerPhone).HasMaxLength(50);

        // SAL-09: the threshold in force at the time, so a later settings change cannot
        // retrospectively make a past discount look unauthorised.
        builder.Property(s => s.DiscountPolicy)
            .HasConversion<ValueConverters.DiscountPolicyConverter>()
            .HasColumnName("MaxUnauthorisedDiscountRate")
            .HasPrecision(5, 4)
            .IsRequired();

        // SAL-10: totals are stored so reporting can aggregate them in SQL, but the only
        // writer is the aggregate itself.
        builder.MapMoney(s => s.GrossAmount, "Gross");
        builder.MapMoney(s => s.DiscountTotal, "Discount");
        builder.MapMoney(s => s.TaxTotal, "Tax");
        builder.MapMoney(s => s.NetTotal, "Net");
        builder.MapMoney(s => s.Total, "Total");
        builder.MapMoney(s => s.CashRoundingAdjustment, "CashRounding");

        ConfigureDiscount(builder.OwnsOne(s => s.SaleDiscount), "SaleDiscount");

        builder.HasMany(s => s.Lines)
            .WithOne()
            .HasForeignKey(l => l.SaleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(s => s.Lines).HasField("_lines").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(s => s.Payments)
            .WithOne()
            .HasForeignKey(p => p.SaleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(s => s.Payments).HasField("_payments").UsePropertyAccessMode(PropertyAccessMode.Field);

        // Derived views over the loaded aggregate, not columns.
        builder.Ignore(s => s.ActiveLines);
        builder.Ignore(s => s.AmountPayable);
        builder.Ignore(s => s.AmountSettled);
        builder.Ignore(s => s.BalanceDue);
        builder.Ignore(s => s.ChangeGiven);
        builder.Ignore(s => s.CostOfGoods);
        builder.Ignore(s => s.IsSettled);
        builder.Ignore(s => s.IsEditable);
        builder.Ignore(s => s.Currency);
        builder.Ignore(s => s.DomainEvents);

        builder.HasIndex(s => s.ShopId);
        builder.HasIndex(s => s.ShiftId);

        // BO-04: looking up a receipt a customer is holding.
        builder.HasIndex(s => new { s.ShopId, s.ReceiptNumber });

        // BO-06 and BO-07 slice by day and by cashier.
        builder.HasIndex(s => new { s.ShopId, s.CompletedAt });
    }

    /// <summary>
    /// SAL-07. Owned rather than a complex type because a sale or a line usually has no
    /// discount, and an owned reference is allowed to be absent.
    /// </summary>
    internal static void ConfigureDiscount<TOwner>(
        OwnedNavigationBuilder<TOwner, Discount> builder,
        string prefix)
        where TOwner : class
    {
        builder.Property(d => d.Kind)
            .HasColumnName(prefix + "Kind")
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(d => d.Percentage)
            .HasColumnName(prefix + "Percentage")
            .HasPrecision(5, 4);

        builder.Property(d => d.Reason).HasColumnName(prefix + "Reason").HasMaxLength(200);

        // SAL-09 and SEC-06: who allowed it is part of the record, not a separate log entry
        // that could go missing.
        builder.Property(d => d.AuthorisedBy)
            .HasColumnName(prefix + "AuthorisedBy")
            .HasConversion<ValueConverters.UserIdConverter>();

        builder.Property(d => d.FixedAmountMinor).HasColumnName(prefix + "FixedMinor");
        builder.Property(d => d.CurrencyCode)
            .HasColumnName(prefix + "Currency")
            .HasMaxLength(MoneyMapping.CurrencyCodeLength)
            .IsFixedLength();

        builder.Ignore(d => d.FixedAmount);
    }
}

internal sealed class SaleLineConfiguration : IEntityTypeConfiguration<SaleLine>
{
    public void Configure(EntityTypeBuilder<SaleLine> builder)
    {
        builder.ToTable("SaleLines");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id)
            .HasConversion<ValueConverters.SaleLineIdConverter>()
            .ValueGeneratedNever();

        builder.Property(l => l.SaleId).HasConversion<ValueConverters.SaleIdConverter>().IsRequired();
        builder.Property(l => l.ProductId).HasConversion<ValueConverters.ProductIdConverter>().IsRequired();
        builder.Property(l => l.StockProductId).HasConversion<ValueConverters.ProductIdConverter>();
        builder.Property(l => l.StockBaseUnit).HasConversion<string>().HasMaxLength(20);
        builder.Property(l => l.VoidedBy).HasConversion<ValueConverters.UserIdConverter>();

        // CAT-05: the catalogue values as they stood, so a reprint next year is honest.
        builder.Property(l => l.Sku).HasMaxLength(50).IsRequired();
        builder.Property(l => l.Description).HasMaxLength(200).IsRequired();
        builder.Property(l => l.Unit).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(l => l.VoidReason).HasMaxLength(500);

        builder.MapQuantity(l => l.Quantity, "Quantity");

        builder.MapMoney(l => l.UnitPrice, "UnitPrice");
        builder.MapMoney(l => l.UnitCost, "UnitCost");
        builder.MapMoney(l => l.ApportionedSaleDiscount, "ApportionedDiscount");

        // CMP-02: the tax rule is copied onto the line, never referenced.
        builder.ComplexProperty(l => l.TaxTreatment, tax =>
        {
            tax.Property(t => t.Code).HasColumnName("TaxCode").HasMaxLength(20);
            tax.Property(t => t.Category).HasColumnName("TaxCategory").HasConversion<string>().HasMaxLength(20);
            tax.Property(t => t.Rate).HasColumnName("TaxRate").HasPrecision(8, 4);
            tax.Ignore(t => t.IsTaxable);
        });

        SaleConfiguration.ConfigureDiscount(builder.OwnsOne(l => l.LineDiscount), "LineDiscount");

        // Everything below is arithmetic over the columns above. Storing it would create a
        // second version of the truth that could drift from the first.
        builder.Ignore(l => l.GrossAmount);
        builder.Ignore(l => l.LineDiscountAmount);
        builder.Ignore(l => l.TotalDiscountAmount);
        builder.Ignore(l => l.LineTotal);
        builder.Ignore(l => l.TaxAmount);
        builder.Ignore(l => l.NetAmount);
        builder.Ignore(l => l.CostAmount);
        builder.Ignore(l => l.Currency);

        builder.HasIndex(l => l.SaleId);

        // BO-07 reports by product.
        builder.HasIndex(l => l.ProductId);
    }
}

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
            .HasConversion<ValueConverters.PaymentIdConverter>()
            .ValueGeneratedNever();

        builder.Property(p => p.SaleId).HasConversion<ValueConverters.SaleIdConverter>().IsRequired();
        builder.Property(p => p.Method).HasConversion<string>().HasMaxLength(20).IsRequired();

        // CMP-06 and PAY-08: the approval code and nothing else. There is deliberately no
        // column here that a card number could be put in.
        builder.Property(p => p.Reference).HasMaxLength(64);

        builder.MapMoney(p => p.Amount, "Amount");
        builder.MapMoney(p => p.Tendered, "Tendered");

        builder.Ignore(p => p.Change);

        builder.HasIndex(p => p.SaleId);
    }
}
