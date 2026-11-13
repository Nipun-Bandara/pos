using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RetailCore.Application.Foundation;
using RetailCore.Domain.Catalogue;
using RetailCore.Domain.Common;
using RetailCore.Domain.Monetary;
using RetailCore.Domain.Security;
using RetailCore.Domain.Tax;

namespace RetailCore.Application.Catalogue;

public sealed partial class CatalogueService
{
    public Task<ProductImportPreview> PreviewImportAsync(byte[] bytes, string extension, CategoryId category,
        CancellationToken cancellationToken = default) => transactions.ReadAsync(async (store, ct) =>
    {
        var actor = await foundation.RequireUserAsync(store, true, ct).ConfigureAwait(false);
        return await ValidateImportAsync(store, actor.ShopId, bytes, extension, category, ct).ConfigureAwait(false);
    }, cancellationToken);

    public Task<int> ImportAsync(Guid operation, byte[] bytes, string extension, CategoryId category,
        CancellationToken cancellationToken = default)
    {
        // Own the bytes for the duration of retries; caller mutation must not change the command.
        var content = bytes.ToArray();
        return transactions.WriteAsync(async (store, ct) =>
        {
            var actor = await foundation.RequireUserAsync(store, true, ct).ConfigureAwait(false);
            if (operation == Guid.Empty) throw new FoundationException("OperationConflict");
            var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            { Actor = actor.Id, category, Extension = extension.ToLowerInvariant(), Hash = Convert.ToHexString(SHA256.HashData(content)) }))));
            var prior = await store.CommandAsync(operation, ct).ConfigureAwait(false);
            if (prior is not null)
            {
                if (prior.Kind != "ProductImport" || prior.Fingerprint != fingerprint) throw new FoundationException("OperationConflict");
                return JsonSerializer.Deserialize<int>(prior.Result);
            }
            var preview = await ValidateImportAsync(store, actor.ShopId, content, extension, category, ct).ConfigureAwait(false);
            if (!preview.CanImport) throw new FoundationException("ImportInvalidRows");
            var shop = (await store.ShopsAsync(ct).ConfigureAwait(false)).Single();
            var rates = await store.TaxRatesAsync(actor.ShopId, ct).ConfigureAwait(false);
            var noTax = rates.FirstOrDefault(t => t.Code == "NO-TAX" && t.Category == TaxCategory.Exempt && t.EffectiveTo == null);
            if (noTax is null)
            {
                noTax = TaxRate.Create(actor.ShopId, "NO-TAX", "බදු නැත / No tax", TaxCategory.Exempt, 0m, DateOnly.MinValue);
                store.Add(noTax);
            }
            foreach (var row in preview.Rows)
            {
                var input = row.Product!;
                var product = Product.Create(actor.ShopId, input.Sku, input.EnglishName, category, input.Unit,
                    Money.FromMajor(input.NormalPrice, shop.Currency), Money.FromMajor(input.Cost, shop.Currency), noTax.Id);
                Apply(product, input, shop.Currency); store.Add(product);
                store.Audit(AuditEntry.Record(actor.ShopId, AuditAction.ProductChanged, "Product", product.Id.Value,
                    clock.UtcNow, actor.Id, detailsJson: JsonSerializer.Serialize(new { Import = operation, row.Row, Product = input })));
            }
            store.CompleteCommand(operation, "ProductImport", fingerprint, JsonSerializer.Serialize(preview.Rows.Count));
            return preview.Rows.Count;
        }, cancellationToken);
    }

    private static async Task<ProductImportPreview> ValidateImportAsync(ILocalStore store, ShopId shop,
        byte[] bytes, string extension, CategoryId category, CancellationToken ct)
    {
        var categories = await store.CategoriesAsync(shop, ct).ConfigureAwait(false);
        if (!categories.Any(c => c.Id == category && c.IsActive)) throw new FoundationException("InvalidCategory");
        var products = await store.ProductsAsync(shop, ct).ConfigureAwait(false);
        var currency = (await store.ShopsAsync(ct).ConfigureAwait(false)).Single().Currency.Code;
        var skus = products.Select(p => p.Sku).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var barcodes = products.SelectMany(p => p.Barcodes.Select(b => b.Code).Append(p.PrimaryBarcode ?? ""))
            .Where(b => b.Length > 0).ToHashSet(StringComparer.Ordinal);
        var rows = ProductImportFile.Read(bytes, extension, category).Select(row =>
        {
            if (row.Product is not { } input) return row;
            try
            {
                // Validate the complete domain definition without changing a saved product.
                Preview(input, currency, 1);
                if (!skus.Add(input.Sku.Trim())) return row with { Error = "DuplicateSku" };
                if (!barcodes.Add(input.Barcode.Trim())) return row with { Error = "DuplicateBarcode" };
                return row;
            }
            catch (FoundationException ex) { return row with { Error = ex.Key }; }
        }).ToArray();
        return new(rows);
    }
}
