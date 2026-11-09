using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RetailCore.Domain.Catalogue;
using RetailCore.Domain.Tax;

namespace RetailCore.Infrastructure.Persistence.Configurations;

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("Categories");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id)
            .HasConversion<ValueConverters.CategoryIdConverter>()
            .ValueGeneratedNever();

        builder.Property(c => c.ShopId).HasConversion<ValueConverters.ShopIdConverter>().IsRequired();
        builder.Property(c => c.ParentId).HasConversion<ValueConverters.CategoryIdConverter>();
        builder.Property(c => c.Name).HasMaxLength(100).IsRequired();
        builder.Property(c => c.SinhalaName).HasMaxLength(100).IsRequired();

        builder.HasIndex(c => c.ShopId);
    }
}

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
            .HasConversion<ValueConverters.ProductIdConverter>()
            .ValueGeneratedNever();

        builder.Property(p => p.ShopId).HasConversion<ValueConverters.ShopIdConverter>().IsRequired();
        builder.Property(p => p.CategoryId).HasConversion<ValueConverters.CategoryIdConverter>().IsRequired();
        builder.Property(p => p.TaxRateId).HasConversion<ValueConverters.TaxRateIdConverter>().IsRequired();

        builder.Property(p => p.Sku).HasMaxLength(50).IsRequired();
        builder.Property(p => p.Name).HasMaxLength(200).IsRequired();
        builder.Property(p => p.SinhalaName).HasMaxLength(200).IsRequired();
        builder.Property(p => p.ReceiptName).HasMaxLength(200).IsRequired();
        builder.Property(p => p.PrimaryBarcode).HasMaxLength(64);
        builder.HasIndex(p => p.PrimaryBarcode).IsUnique();
        builder.OwnsMany(p => p.PriceBands, band =>
        {
            band.ToTable("ProductPriceBands");
            band.WithOwner().HasForeignKey("ProductId");
            band.Property<int>("Id").ValueGeneratedOnAdd();
            band.HasKey("Id");
            band.Property(b => b.CurrencyCode).HasMaxLength(3);
            band.HasIndex("ProductId", nameof(QuantityPriceBand.MinimumThousandths)).IsUnique();
        });
        builder.Navigation(p => p.PriceBands).HasField("_priceBands").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Property(p => p.Unit).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.MapMoney(p => p.UnitPrice, "UnitPrice");
        builder.MapMoney(p => p.UnitCost, "UnitCost");

        builder.MapQuantity(p => p.ReorderLevel, "ReorderLevel");

        // CAT-07: several barcodes for one product, because manufacturers change them and
        // the old stock stays on the shelf for months. Its own table so the barcode lookup
        // behind SAL-01 is an index seek and stays inside the 100ms scan budget.
        builder.OwnsMany(p => p.Barcodes, barcode =>
        {
            barcode.ToTable("ProductBarcodes");
            barcode.WithOwner().HasForeignKey("ProductId");
            barcode.Property<int>("Id").ValueGeneratedOnAdd();
            barcode.HasKey("Id");
            barcode.Property(b => b.Code).HasColumnName("Barcode").HasMaxLength(64).IsRequired();
            barcode.HasIndex(b => b.Code);
        });

        builder.Navigation(p => p.Barcodes).HasField("_barcodes").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(p => p.IsWeighed);

        // SAL-02: the shop's own code is how staff look things up, and it must be unique.
        builder.HasIndex(p => new { p.ShopId, p.Sku }).IsUnique();
        builder.HasIndex(p => new { p.ShopId, p.Name });
    }
}

internal sealed class TaxRateConfiguration : IEntityTypeConfiguration<TaxRate>
{
    public void Configure(EntityTypeBuilder<TaxRate> builder)
    {
        builder.ToTable("TaxRates");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id)
            .HasConversion<ValueConverters.TaxRateIdConverter>()
            .ValueGeneratedNever();

        builder.Property(t => t.ShopId).HasConversion<ValueConverters.ShopIdConverter>().IsRequired();
        builder.Property(t => t.Code).HasMaxLength(20).IsRequired();
        builder.Property(t => t.Name).HasMaxLength(100).IsRequired();
        builder.Property(t => t.Category).HasConversion<string>().HasMaxLength(20).IsRequired();

        // Four decimal places carries rates like 12.5% without loss.
        builder.Property(t => t.Rate).HasPrecision(8, 4);

        // CMP-02: rates are effective-dated data, so the lookup is by shop, code and date.
        builder.HasIndex(t => new { t.ShopId, t.Code, t.EffectiveFrom });
    }
}
