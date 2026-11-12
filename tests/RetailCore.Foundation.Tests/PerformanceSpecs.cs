using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using RetailCore.Domain.Catalogue;
namespace RetailCore.Foundation.Tests;

public sealed partial class FoundationSpecs
{
    private static readonly JsonSerializerOptions PerformanceJson = new(){WriteIndented=true};
    /// <summary>Disposable real SQLite workload; never runs against shop data. No hardware/UI claim.</summary>
    [Fact(Explicit = true)]
    public async Task Measure_isolated_ten_thousand_product_checkout()
    {
        await using var f = new LocalFixture(); await f.SetupAsync(); await BillingProduct(f);
        await f.Transactions.WriteAsync(async (store, ct) =>
        {
            var original = (await store.ProductsAsync((await store.ShopsAsync(ct)).Single().Id, ct)).Single();
            for (var i = 0; i < 9999; i++)
            {
                var sku = "PERF" + i.ToString("D6", CultureInfo.InvariantCulture); var barcode = "P" + i.ToString("D8", CultureInfo.InvariantCulture);
                var p = Product.Create(original.ShopId, sku, "Tea " + i, original.CategoryId, original.Unit, original.UnitPrice, original.UnitCost, original.TaxRateId);
                p.ConfigureCatalogue(sku, "තේ " + i, "Tea " + i, "තේ " + i, original.CategoryId, original.Unit, original.UnitCost, original.UnitPrice, barcode, [], true); store.Add(p);
            }
            return true;
        });
        var timer = Stopwatch.StartNew(); var catalogue = await Catalogue(f).LoadAsync(); var catalogueMs = timer.Elapsed.TotalMilliseconds;
        var filters = new List<double>(); for (var i = 0; i < 30; i++) { timer.Restart(); _ = catalogue.Products.Where(p => p.SinhalaName.Contains("තේ 99", StringComparison.Ordinal)).ToArray(); filters.Add(timer.Elapsed.TotalMilliseconds); }
        await Billing(f).OpenShiftAsync(Guid.NewGuid(), 1000); var basket = await Billing(f).LoadAsync(); var scans = new List<double>();
        for (var i = 0; i < 30; i++) { timer.Restart(); basket = await Scan(f, basket); scans.Add(timer.Elapsed.TotalMilliseconds); }
        timer.Restart(); await Billing(f).CompleteAsync(Cash(basket, 10000)); var settlementMs = timer.Elapsed.TotalMilliseconds;
        await f.RestartAsync(); timer.Restart(); await f.App.StartAsync(); var restartMs = timer.Elapsed.TotalMilliseconds;
        static double Percentile(List<double> samples) => samples.Order().ElementAt((int)Math.Ceiling(samples.Count * .95) - 1);
        var result = new
        {
            RecordedAt = DateTimeOffset.UtcNow,
            OS = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            Framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            Products = 10000,
            BackendScanP95Ms = Percentile(scans),
            CatalogueLoadMs = catalogueMs,
            InMemoryFilterP95Ms = Percentile(filters),
            SettlementMs = settlementMs,
            ServiceRestartMs = restartMs,
            Boundary = "Real disposable SQLite backend. WPF cold start, UI binding, intended PC and physical scanner/printer require separate measurements."
        };
        var directory = Environment.GetEnvironmentVariable("RETAILCORE_PERFORMANCE_DIR") ?? Path.Combine(Path.GetTempPath(), "retailcore-performance"); Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "checkout-10000.json"), JsonSerializer.Serialize(result, PerformanceJson));
        catalogue.Products.Count.ShouldBe(10000);
    }
}
