using System.Text.Json;
using RetailCore.Application.Abstractions;
using RetailCore.Application.Foundation;
using RetailCore.Domain.Catalogue;
using RetailCore.Domain.Common;
using RetailCore.Domain.Monetary;
using RetailCore.Domain.Security;
using RetailCore.Domain.Tax;

namespace RetailCore.Application.Catalogue;

public sealed record BandInput(decimal Minimum, decimal Price);
public sealed record ProductInput(ProductId? Id, string Sku, string SinhalaName, string EnglishName,
    string ReceiptName, CategoryId CategoryId, UnitOfMeasure Unit, decimal Cost, decimal NormalPrice,
    string Barcode, IReadOnlyList<BandInput> Bands, bool Active = true);
public sealed record ProductRow(ProductInput Product, string Category, string Currency, long StockThousandths, UnitOfMeasure StockUnit = UnitOfMeasure.Each)
{
    public string Sku => Product.Sku;
    public string SinhalaName => Product.SinhalaName;
    public string EnglishName => Product.EnglishName;
    public string Barcode => Product.Barcode;
    public decimal Cost => Product.Cost;
    public decimal NormalPrice => Product.NormalPrice;
    public string Bands => string.Join("; ", Product.Bands.Select(b => $"{b.Minimum:0.###}: {b.Price:0.00}"));
    public decimal Stock => StockThousandths / 1000m;
    public bool Active => Product.Active;
}
public sealed record CategoryRow(CategoryId Id, string SinhalaName, string EnglishName, bool Active)
{
    public string Display => $"{SinhalaName} / {EnglishName}";
}
public sealed record CatalogueData(IReadOnlyList<ProductRow> Products, IReadOnlyList<CategoryRow> Categories, string Currency);

/// <summary>Owner checks and audit share the transaction with every catalogue mutation.</summary>
public sealed class CatalogueService(ILocalTransactions transactions, FoundationService foundation, IClock clock)
{
    public Task<CatalogueData> LoadAsync(CancellationToken cancellationToken = default) =>
        transactions.ReadAsync(async (store, ct) =>
        {
            var actor = await foundation.RequireUserAsync(store, false, ct).ConfigureAwait(false);
            var shop = (await store.ShopsAsync(ct).ConfigureAwait(false)).Single();
            var categories = await store.CategoriesAsync(actor.ShopId, ct).ConfigureAwait(false);
            var stock = await store.StockAsync(actor.ShopId, ct).ConfigureAwait(false);
            var products = await store.ProductsAsync(actor.ShopId, ct).ConfigureAwait(false);
            var rows = products.Select(p => new ProductRow(new ProductInput(p.Id, p.Sku, p.SinhalaName,
                p.Name, p.ReceiptName, p.CategoryId, p.Unit, p.UnitCost.ToDecimal(), p.UnitPrice.ToDecimal(),
                p.PrimaryBarcode ?? (p.Barcodes.Count > 0 ? p.Barcodes[0].Code : null) ?? "",
                p.PriceBands.Select(b => new BandInput(b.MinimumThousandths / 1000m,
                    Money.FromMinor(b.PriceMinor, p.UnitPrice.Currency).ToDecimal())).ToArray(), p.IsActive),
                categories.SingleOrDefault(c => c.Id == p.CategoryId)?.Name ?? "", shop.Currency.Code,
                stock.GetValueOrDefault(p.StockBaseProductId ?? p.Id), p.StockBaseUnit ?? p.Unit)).OrderBy(p => p.Sku, StringComparer.Ordinal).ToArray();
            return new CatalogueData(rows, categories.Select(c => new CategoryRow(c.Id, c.SinhalaName, c.Name, c.IsActive))
                .OrderBy(c => c.EnglishName, StringComparer.Ordinal).ToArray(), shop.Currency.Code);
        }, cancellationToken);

    public Task<CategoryId> SaveCategoryAsync(CategoryId? id, string sinhala, string english, bool active,
        CancellationToken cancellationToken = default) => transactions.WriteAsync(async (store, ct) =>
        {
            var actor = await foundation.RequireUserAsync(store, true, ct).ConfigureAwait(false);
            var categories = await store.CategoriesAsync(actor.ShopId, ct).ConfigureAwait(false);
            if (id is not null && !categories.Any(c => c.Id == id)) throw new FoundationException("InvalidCategory");
            try
            {
                var category = id is null ? Category.Create(actor.ShopId, english) : categories.Single(c => c.Id == id);
                category.ConfigureNames(sinhala, english, active);
                if (id is null) store.Add(category);
                store.Audit(AuditEntry.Record(actor.ShopId, AuditAction.CategoryChanged, "Category", category.Id.Value,
                    clock.UtcNow, actor.Id, detailsJson: JsonSerializer.Serialize(new { sinhala, english, active })));
                return category.Id;
            }
            catch (DomainException) { throw new FoundationException("InvalidCategory"); }
            catch (ArgumentException) { throw new FoundationException("InvalidCategory"); }
        }, cancellationToken);

    public Task<ProductId> SaveProductAsync(ProductInput input, CancellationToken cancellationToken = default) =>
        transactions.WriteAsync(async (store, ct) =>
        {
            var actor = await foundation.RequireUserAsync(store, true, ct).ConfigureAwait(false);
            var shop = (await store.ShopsAsync(ct).ConfigureAwait(false)).Single();
            var products = await store.ProductsAsync(actor.ShopId, ct).ConfigureAwait(false);
            if (input.Id is not null && !products.Any(p => p.Id == input.Id)) throw new FoundationException("InvalidProduct");
            if (products.Any(p => p.Id != input.Id && string.Equals(p.Sku, input.Sku.Trim(), StringComparison.OrdinalIgnoreCase)))
                throw new FoundationException("DuplicateSku");
            if (products.Any(p => p.Id != input.Id && p.HasBarcode(input.Barcode.Trim())))
                throw new FoundationException("DuplicateBarcode");
            var categories = await store.CategoriesAsync(actor.ShopId, ct).ConfigureAwait(false);
            // Existing products may retain their deactivated category while being edited or deactivated.
            if (!categories.Any(c => c.Id == input.CategoryId && (c.IsActive ||
                products.Any(p => p.Id == input.Id && p.CategoryId == c.Id))))
                throw new FoundationException("InvalidCategory");
            try
            {
                var taxRates = await store.TaxRatesAsync(actor.ShopId, ct).ConfigureAwait(false);
                var noTax = taxRates.FirstOrDefault(t => t.Code == "NO-TAX" && t.Category == TaxCategory.Exempt && t.EffectiveTo == null);
                if (noTax is null)
                {
                    noTax = TaxRate.Create(actor.ShopId, "NO-TAX", "බදු නැත / No tax", TaxCategory.Exempt, 0m, DateOnly.MinValue);
                    store.Add(noTax);
                }
                var product = input.Id is null
                    ? Product.Create(actor.ShopId, input.Sku, input.EnglishName, input.CategoryId, input.Unit,
                        Money.FromMajor(input.NormalPrice, shop.Currency), Money.FromMajor(input.Cost, shop.Currency), noTax.Id)
                    : products.Single(p => p.Id == input.Id);
                if (product.Unit != input.Unit && (await store.MovementsAsync(actor.ShopId, ct).ConfigureAwait(false))
                    .Any(m => m.ProductId == product.Id || m.EnteredProductId == product.Id))
                    throw new FoundationException("StockMappingLocked");
                Apply(product, input, shop.Currency);
                if (input.Id is null) store.Add(product);
                store.Audit(AuditEntry.Record(actor.ShopId, AuditAction.ProductChanged, "Product", product.Id.Value,
                    clock.UtcNow, actor.Id, detailsJson: JsonSerializer.Serialize(input)));
                return product.Id;
            }
            catch (DomainException ex) { throw new FoundationException(ex.Message is "InvalidPricing" or "StockMappingLocked" ? ex.Message : "InvalidProduct"); }
            catch (ArgumentException) { throw new FoundationException("InvalidProduct"); }
            catch (OverflowException) { throw new FoundationException("InvalidProduct"); }
        }, cancellationToken);

    public static PriceQuote Preview(ProductInput input, string currency, decimal quantity)
    {
        Quantity requested;
        try { requested = Quantity.Of(quantity); }
        catch (DomainException) { throw new FoundationException("InvalidQuantity"); }
        catch (OverflowException) { throw new FoundationException("InvalidQuantity"); }
        if (!requested.IsPositive || (!input.Unit.AllowsFractionalQuantity() && !requested.IsWholeUnits))
            throw new FoundationException("InvalidQuantity");
        try
        {
            var money = Currency.FromCode(currency);
            var product = Product.Create(ShopId.New(), input.Sku, input.EnglishName, input.CategoryId, input.Unit,
                Money.FromMajor(input.NormalPrice, money), Money.FromMajor(input.Cost, money), TaxRateId.New());
            Apply(product, input, money);
            return QuantityPricing.Preview(product, requested);
        }
        catch (DomainException ex) { throw new FoundationException(ex.Message == "InvalidQuantity" ? "InvalidQuantity" : "InvalidPricing"); }
        catch (ArgumentException) { throw new FoundationException("InvalidProduct"); }
        catch (OverflowException) { throw new FoundationException("InvalidPricing"); }
    }

    private static void Apply(Product product, ProductInput input, Currency currency) =>
        product.ConfigureCatalogue(input.Sku, input.SinhalaName, input.EnglishName, input.ReceiptName,
            input.CategoryId, input.Unit, Money.FromMajor(input.Cost, currency), Money.FromMajor(input.NormalPrice, currency),
            input.Barcode, input.Bands.Select(b => new QuantityPriceBand(Quantity.Of(b.Minimum),
                Money.FromMajor(b.Price, currency))).ToArray(), input.Active);
}
