using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailCore.Application.Catalogue;
using RetailCore.Application.Foundation;
using RetailCore.Domain.Catalogue;
using RetailCore.Domain.Common;
using RetailCore.Domain.Security;
using RetailCore.Infrastructure.Sqlite;

namespace RetailCore.Foundation.Tests;

public sealed partial class FoundationSpecs
{
    [Theory]
    [InlineData(".csv")]
    [InlineData(".xlsx")]
    public async Task Product_import_preserves_Sinhala_text_codes_tiers_and_zero_stock_after_restart_and_safe_retry(string extension)
    {
        await using var f = new LocalFixture(); var owner = await f.SetupAsync();
        var service = Catalogue(f); var category = await service.SaveCategoryAsync(null, "පාන", "Drinks", true);
        var bytes = ProductImportFile.Template(extension); var operation = Guid.NewGuid();
        var preview = await service.PreviewImportAsync(bytes, extension, category);
        preview.CanImport.ShouldBeTrue(); preview.Rows.Single().Product!.Barcode.ShouldBe("001234567890");
        (await service.LoadAsync()).Products.ShouldBeEmpty();
        var audits = await f.AuditCountAsync();
        (await service.ImportAsync(operation, bytes, extension, category)).ShouldBe(1);
        (await f.AuditCountAsync()).ShouldBe(audits + 1);
        await f.RestartAsync(); await f.App.StartAsync(); await f.App.SignInAsync(owner.Id, Pin);
        service = Catalogue(f);
        var beforeRetry = await f.AuditCountAsync();
        (await service.ImportAsync(operation, bytes, extension, category)).ShouldBe(1);
        (await f.AuditCountAsync()).ShouldBe(beforeRetry);
        var row = (await service.LoadAsync()).Products.Single();
        row.Product.ReceiptName.ShouldBe("තේ පැකට්"); row.SinhalaName.ShouldBe("තේ");
        row.Barcode.ShouldBe("001234567890"); row.StockThousandths.ShouldBe(0);
        new[] { 1, 2, 3, 4, 5, 2 }.Select(q => CatalogueService.Preview(row.Product, "LKR", q).Total.ToDecimal())
            .ShouldBe(new decimal[] { 100, 180, 255, 320, 400, 180 });
        await using var scope = f.Provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TillDbContext>();
        (await db.StockMovements.IgnoreQueryFilters().CountAsync()).ShouldBe(0);
        (await db.TaxRates.IgnoreQueryFilters().SingleAsync()).Rate.ShouldBe(0);
    }

    private static byte[] ImportCsv(params string[] rows) => Encoding.UTF8.GetBytes(string.Join(',', ProductImportFile.Columns) + "\n" + string.Join('\n', rows));
    private const string ImportTea = "00001,තේ,Tea,තේ පැකට්,000001,Each,50,100,2,90,3,85,4,80,true";

    [Fact]
    public async Task Import_validates_all_rows_and_rechecks_duplicates_category_and_current_authority_at_commit()
    {
        await using var f = new LocalFixture(); var owner = await f.SetupAsync();
        var service = Catalogue(f); var category = await service.SaveCategoryAsync(null, "පාන", "Drinks", true);
        var bytes = ImportCsv(ImportTea, "OTHER" + ImportTea[5..]);
        var audits = await f.AuditCountAsync();
        var preview = await service.PreviewImportAsync(bytes, ".csv", category);
        preview.CanImport.ShouldBeFalse(); preview.Rows[1].Error.ShouldBe("DuplicateBarcode");
        (await Should.ThrowAsync<FoundationException>(() => service.ImportAsync(Guid.NewGuid(), bytes, ".csv", category))).Key.ShouldBe("ImportInvalidRows");
        (await f.AuditCountAsync()).ShouldBe(audits); (await service.LoadAsync()).Products.ShouldBeEmpty();
        bytes = ImportCsv(ImportTea, ImportTea.Replace("000001", "000002", StringComparison.Ordinal));
        (await service.PreviewImportAsync(bytes, ".csv", category)).Rows[1].Error.ShouldBe("DuplicateSku");
        bytes = ImportCsv(ImportTea);
        (await service.PreviewImportAsync(bytes, ".csv", category)).CanImport.ShouldBeTrue();
        var operation = Guid.NewGuid(); await service.ImportAsync(operation, bytes, ".csv", category);
        (await service.PreviewImportAsync(bytes, ".csv", category)).CanImport.ShouldBeFalse();
        (await Should.ThrowAsync<FoundationException>(() => service.ImportAsync(Guid.NewGuid(), bytes, ".csv", category))).Key.ShouldBe("ImportInvalidRows");
        (await Should.ThrowAsync<FoundationException>(() => service.ImportAsync(operation, ImportCsv(ImportTea + " "), ".csv", category))).Key.ShouldBe("OperationConflict");
        await f.App.CreateUserAsync("Cashier", "123789", Role.Cashier, "en");
        var cashier = (await f.App.UsersAsync()).Single(u => u.Role == Role.Cashier);
        await f.App.SignInAsync(cashier.Id, "123789");
        (await Should.ThrowAsync<FoundationException>(() => service.PreviewImportAsync(bytes, ".csv", category))).Key.ShouldBe("OwnerRequired");
        (await Should.ThrowAsync<FoundationException>(() => service.ImportAsync(operation, bytes, ".csv", category))).Key.ShouldBe("OwnerRequired");
        f.App.Lock();
        (await Should.ThrowAsync<FoundationException>(() => service.ImportAsync(operation, bytes, ".csv", category))).Key.ShouldBe("SignInRequired");
        await f.App.SignInAsync(owner.Id, Pin);
        await service.SaveCategoryAsync(category, "පාන", "Drinks", false);
        (await Should.ThrowAsync<FoundationException>(() => service.ImportAsync(Guid.NewGuid(), bytes, ".csv", category))).Key.ShouldBe("InvalidCategory");
    }

    [Theory]
    [InlineData("Each", "2", "3", "4", "true", "50.001")]
    [InlineData("Each", "2", "1", "4", "true", "50")]
    [InlineData("Each", "0.5", "3", "4", "true", "50")]
    [InlineData("InvalidUnit", "2", "3", "4", "true", "50")]
    [InlineData("Each", "2", "3", "4", "unknown", "50")]
    public async Task Invalid_import_financial_precision_bands_and_units_never_partially_save(string unit, string first, string second, string third, string active, string cost)
    {
        await using var f = new LocalFixture(); await f.SetupAsync(); var service = Catalogue(f);
        var category = await service.SaveCategoryAsync(null, "පාන", "Drinks", true);
        var bytes = ImportCsv(ImportTea, $"B,තේ,Tea,තේ,000002,{unit},{cost},100,{first},90,{second},85,{third},80,{active}");
        (await service.PreviewImportAsync(bytes, ".csv", category)).CanImport.ShouldBeFalse();
        var audits = await f.AuditCountAsync();
        await Should.ThrowAsync<FoundationException>(() => service.ImportAsync(Guid.NewGuid(), bytes, ".csv", category));
        (await service.LoadAsync()).Products.ShouldBeEmpty(); (await f.AuditCountAsync()).ShouldBe(audits);
    }

    [Fact]
    public async Task Valid_fractional_bands_and_quoted_CSV_names_import_without_losing_precision()
    {
        await using var f = new LocalFixture(); await f.SetupAsync(); var service = Catalogue(f);
        var category = await service.SaveCategoryAsync(null, "තොග", "Bulk", true);
        var bytes = ImportCsv("001,සහල්,\"Rice, \"\"white\"\"\",සහල්,00011,Kilogram,50.01,100,0.5,90,1.250,85,,,true");
        var preview = await service.PreviewImportAsync(bytes, ".csv", category);
        preview.CanImport.ShouldBeTrue();
        await service.ImportAsync(Guid.NewGuid(), bytes, ".csv", category);
        var product = (await service.LoadAsync()).Products.Single().Product;
        product.EnglishName.ShouldBe("Rice, \"white\""); product.Cost.ShouldBe(50.01m);
        CatalogueService.Preview(product, "LKR", 1.250m).Total.ToDecimal().ShouldBe(106.25m);
    }

    [Fact]
    public async Task Import_failure_after_SQL_rolls_back_entire_batch_tax_audit_and_retry_identity()
    {
        var failure = new FailAfterSave(); await using var f = new LocalFixture(failure); await f.SetupAsync();
        var service = Catalogue(f); var category = await service.SaveCategoryAsync(null, "පාන", "Drinks", true);
        var bytes = ImportCsv(ImportTea, "B,තේ,Tea,තේ,000002,Each,50,100,,,,,,,true"); var operation = Guid.NewGuid();
        var audits = await f.AuditCountAsync(); failure.Armed = true;
        await Should.ThrowAsync<InvalidOperationException>(() => service.ImportAsync(operation, bytes, ".csv", category));
        (await service.LoadAsync()).Products.ShouldBeEmpty(); (await f.AuditCountAsync()).ShouldBe(audits);
        await using (var scope = f.Provider.CreateAsyncScope())
            (await scope.ServiceProvider.GetRequiredService<TillDbContext>().TaxRates.IgnoreQueryFilters().CountAsync()).ShouldBe(0);
        (await service.ImportAsync(operation, bytes, ".csv", category)).ShouldBe(2);
        (await f.AuditCountAsync()).ShouldBe(audits + 2);
    }

    [Fact]
    public void Import_rejects_corrupt_oversized_malformed_or_formula_files_and_numeric_Excel_identifiers()
    {
        var category = CategoryId.New();
        foreach (var bytes in new[] { Array.Empty<byte>(), new byte[ProductImportFile.MaximumBytes + 1], Encoding.UTF8.GetBytes("invalid,headers\n1,2"), Encoding.UTF8.GetBytes("\"unclosed") })
            Should.Throw<FoundationException>(() => ProductImportFile.Read(bytes, ".csv", category));
        Should.Throw<FoundationException>(() => ProductImportFile.Read([0xFF, 0xFF], ".csv", category));
        Should.Throw<FoundationException>(() => ProductImportFile.Read([1, 2, 3], ".xlsx", category));
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        foreach (var formula in new[] { true, false })
        {
            var bytes = ChangeImportSheet(sheet =>
            {
                var cell = sheet.Descendants(ns + "row").Last().Elements(ns + "c").First();
                if (formula) cell.Add(new XElement(ns + "f", "1+1"));
                else { cell.RemoveNodes(); cell.SetAttributeValue("t", "n"); cell.Add(new XElement(ns + "v", "123")); }
            });
            Should.Throw<FoundationException>(() => ProductImportFile.Read(bytes, ".xlsx", category)).Key
                .ShouldBe(formula ? "ImportFormulas" : "ImportTextIdentifiers");
        }
    }

    [Fact]
    public void Excel_shared_strings_sparse_cells_and_typed_active_flag_preserve_identifiers()
    {
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var bytes = ChangeImportSheet(sheet =>
        {
            var cells = sheet.Descendants(ns + "row").Last().Elements(ns + "c").ToArray();
            for (var i = 0; i < cells.Length; i++) cells[i].SetAttributeValue("r", ((char)('A' + i)) + "4");
            for (var i = 8; i < 14; i++) cells[i].Remove();
            cells[0].RemoveNodes(); cells[0].SetAttributeValue("t", "s"); cells[0].Add(new XElement(ns + "v", "0"));
            cells[14].RemoveNodes(); cells[14].SetAttributeValue("t", "b"); cells[14].Add(new XElement(ns + "v", "1"));
        });
        using var stream = new MemoryStream(); stream.Write(bytes); stream.Position = 0;
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Update, true))
        {
            using var output = zip.CreateEntry("xl/sharedStrings.xml").Open();
            new XDocument(new XElement(ns + "sst", new XElement(ns + "si",
                new XElement(ns + "r", new XElement(ns + "t", "00")),
                new XElement(ns + "r", new XElement(ns + "t", "001"))))).Save(output);
        }
        var product = ProductImportFile.Read(stream.ToArray(), ".xlsx", CategoryId.New()).Single().Product!;
        product.Sku.ShouldBe("00001"); product.Barcode.ShouldBe("001234567890");
        product.Bands.ShouldBeEmpty(); product.Active.ShouldBeTrue();
    }

    private static byte[] ChangeImportSheet(Action<XDocument> change)
    {
        using var stream = new MemoryStream(); stream.Write(ProductImportFile.Template(".xlsx")); stream.Position = 0;
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Update, true))
        {
            var entry = zip.GetEntry("xl/worksheets/sheet1.xml")!; XDocument sheet;
            using (var input = entry.Open()) sheet = XDocument.Load(input);
            change(sheet); entry.Delete(); using var output = zip.CreateEntry("xl/worksheets/sheet1.xml").Open(); sheet.Save(output);
        }
        return stream.ToArray();
    }
}
