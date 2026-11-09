using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using RetailCore.Application.Catalogue;
using RetailCore.Application.Foundation;
using RetailCore.Domain.Catalogue;
using RetailCore.Domain.Common;
using RetailCore.Domain.Inventory;
using RetailCore.Domain.Monetary;
using RetailCore.Domain.Sales;
using RetailCore.Domain.Security;
using RetailCore.Domain.Tax;
using RetailCore.Infrastructure.Sqlite;

namespace RetailCore.Foundation.Tests;

public sealed partial class FoundationSpecs
{
    private static ProductInput Tea(CategoryId category) => new(null, "TEA-01", "තේ", "Tea", "තේ පැකට්", category,
        UnitOfMeasure.Each, 50, 100, "001234567890", [new(2, 90), new(3, 85), new(4, 80)]);
    private static CatalogueService Catalogue(LocalFixture f) => f.Provider.GetRequiredService<CatalogueService>();

    [Fact]
    public async Task Catalogue_acceptance_persists_bands_names_barcode_edits_and_deactivation_after_restart()
    {
        await using var f = new LocalFixture();
        var owner = await f.SetupAsync();
        var service = Catalogue(f);
        var category = await service.SaveCategoryAsync(null, "පාන", "Drinks", true);
        var input = Tea(category);
        var id = await service.SaveProductAsync(input);
        var row = (await service.LoadAsync()).Products.Single();
        System.Text.Json.JsonSerializer.Serialize(row.Product).ShouldBe(System.Text.Json.JsonSerializer.Serialize(input with { Id = id }));
        await service.SaveProductAsync(row.Product with { Bands = [new(2, 89), new(3, 84), new(4, 79)] });
        await service.SaveProductAsync(row.Product);
        row.StockThousandths.ShouldBe(0);
        new[] { 1, 2, 3, 4, 5, 2 }.Select(q => CatalogueService.Preview(row.Product, "LKR", q).Total.ToDecimal())
            .ShouldBe(new decimal[] { 100, 180, 255, 320, 400, 180 });
        await f.RestartAsync();
        await f.App.StartAsync();
        await f.App.SignInAsync(owner.Id, Pin);
        row = (await Catalogue(f).LoadAsync()).Products.Single();
        row.Barcode.ShouldBe("001234567890");
        row.Product.Bands.Select(b => b.Price).ShouldBe(new decimal[] { 90, 85, 80 });
        await Catalogue(f).SaveCategoryAsync(category, "බීම", "Beverages", false);
        await Catalogue(f).SaveProductAsync(row.Product with { Active = false, EnglishName = "Updated tea", Barcode = "000009" });
        var data = await Catalogue(f).LoadAsync();
        data.Products.Single().Active.ShouldBeFalse();
        data.Categories.Single().Active.ShouldBeFalse();
        data.Products.Single().Barcode.ShouldBe("000009");
        await using var scope = f.Provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TillDbContext>();
        var tax = await context.TaxRates.IgnoreQueryFilters().SingleAsync();
        tax.Category.ShouldBe(TaxCategory.Exempt);
        tax.Rate.ShouldBe(0);
        (await context.AuditEntries.IgnoreQueryFilters().CountAsync(a => a.Action == AuditAction.ProductChanged)).ShouldBe(4);
    }

    [Fact]
    public async Task Duplicate_skus_and_all_barcodes_remain_reserved_even_when_inactive_and_failed_commands_leave_no_audit()
    {
        await using var f = new LocalFixture();
        await f.SetupAsync();
        var service = Catalogue(f);
        var category = await service.SaveCategoryAsync(null, "පාන", "Drinks", true);
        var input = Tea(category) with { Active = false };
        var id = await service.SaveProductAsync(input);
        var audits = await f.AuditCountAsync();
        (await Should.ThrowAsync<FoundationException>(() => service.SaveProductAsync(input with { Sku = " tea-01 ", Barcode = "OTHER" })))
            .Key.ShouldBe("DuplicateSku");
        (await Should.ThrowAsync<FoundationException>(() => service.SaveProductAsync(input with { Sku = "OTHER" })))
            .Key.ShouldBe("DuplicateBarcode");
        await Should.ThrowAsync<FoundationException>(() => service.SaveProductAsync(input with { Id = id, Bands = [new(2, 90), new(1, 80)] }));
        await Should.ThrowAsync<FoundationException>(() => service.SaveProductAsync(input with { Id = id, ReceiptName = "" }));
        await Should.ThrowAsync<FoundationException>(() => service.SaveProductAsync(input with { Id = id, Cost = -.01m }));
        (await f.AuditCountAsync()).ShouldBe(audits);
        (await service.LoadAsync()).Products.Single().Product.Bands.Count.ShouldBe(3);
        await using (var scope = f.Provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TillDbContext>();
            var p = await context.Products.IgnoreQueryFilters().SingleAsync();
            p.AddBarcode("LEGACY-SECONDARY");
            await context.SaveChangesAsync();
        }
        (await Should.ThrowAsync<FoundationException>(() => service.SaveProductAsync(input with { Sku = "OTHER", Barcode = "LEGACY-SECONDARY" })))
            .Key.ShouldBe("DuplicateBarcode");
    }

    [Fact]
    public async Task Catalogue_commands_recheck_owner_role_lock_and_revoked_credentials_inside_transaction()
    {
        await using var f = new LocalFixture();
        var owner = await f.SetupAsync();
        var service = Catalogue(f);
        var category = await service.SaveCategoryAsync(null, "පාන", "Drinks", true);
        await f.App.CreateUserAsync("Cashier", "123789", Role.Cashier, "en");
        var cashier = (await f.App.UsersAsync()).Single(u => u.Role == Role.Cashier);
        await f.App.SignInAsync(cashier.Id, "123789");
        (await Should.ThrowAsync<FoundationException>(() => service.SaveProductAsync(Tea(category)))).Key.ShouldBe("OwnerRequired");
        (await Should.ThrowAsync<FoundationException>(() => service.SaveCategoryAsync(category, "පාන", "Drinks", false))).Key.ShouldBe("OwnerRequired");
        (await service.LoadAsync()).Products.ShouldBeEmpty();
        f.App.Lock();
        (await Should.ThrowAsync<FoundationException>(() => service.LoadAsync())).Key.ShouldBe("SignInRequired");
        (await Should.ThrowAsync<FoundationException>(() => service.SaveProductAsync(Tea(category)))).Key.ShouldBe("SignInRequired");
        await f.App.SignInAsync(owner.Id, Pin);
        await f.App.CreateUserAsync("Other owner", "832194", Role.Owner, "en");
        var other = (await f.App.UsersAsync()).Single(u => u.Name == "Other owner");
        await using var provider = f.AnotherSession();
        var otherApp = provider.GetRequiredService<FoundationService>();
        await otherApp.StartAsync();
        await otherApp.SignInAsync(other.Id, "832194");
        await otherApp.UpdateUserAsync(owner.Id, Role.Cashier, true, null, false);
        (await Should.ThrowAsync<FoundationException>(() => service.SaveProductAsync(Tea(category)))).Key.ShouldBe("OwnerRequired");
    }

    [Fact]
    public async Task Failed_catalogue_write_rolls_back_product_tax_bands_barcodes_and_audit()
    {
        var failure = new FailAfterSave();
        await using var f = new LocalFixture(failure);
        await f.SetupAsync();
        var service = Catalogue(f);
        var category = await service.SaveCategoryAsync(null, "පාන", "Drinks", true);
        var audits = await f.AuditCountAsync();
        failure.Armed = true;
        await Should.ThrowAsync<InvalidOperationException>(() => service.SaveProductAsync(Tea(category)));
        (await service.LoadAsync()).Products.ShouldBeEmpty();
        (await f.AuditCountAsync()).ShouldBe(audits);
        await using (var scope = f.Provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TillDbContext>();
            (await context.TaxRates.IgnoreQueryFilters().CountAsync()).ShouldBe(0);
            (await context.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM ProductPriceBands").SingleAsync()).ShouldBe(0);
            (await context.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM ProductBarcodes").SingleAsync()).ShouldBe(0);
        }
        await service.SaveProductAsync(Tea(category));
        (await service.LoadAsync()).Products.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Stock_table_projection_preserves_negative_fractional_signed_balances()
    {
        await using var f = new LocalFixture();
        var owner = await f.SetupAsync();
        var service = Catalogue(f);
        var category = await service.SaveCategoryAsync(null, "පාන", "Drinks", true);
        var id = await service.SaveProductAsync(Tea(category) with { Unit = UnitOfMeasure.Kilogram, Bands = [new(.5m, 90)] });
        await using (var scope = f.Provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TillDbContext>();
            var shop = (await context.Shops.SingleAsync()).Id;
            context.StockMovements.Add(StockMovement.Manual(shop, id, Quantity.Of(1.25m), StockDirection.In,
                StockMovementKind.OpeningBalance, "Test opening", owner.Id, DateTimeOffset.UtcNow));
            context.StockMovements.Add(StockMovement.ForSale(shop, id, Quantity.Of(2.5m), SaleId.New(), DateTimeOffset.UtcNow));
            await context.SaveChangesAsync();
        }
        (await service.LoadAsync()).Products.Single().StockThousandths.ShouldBe(-1250);
    }

    [Fact]
    public async Task Completed_sale_persists_price_and_name_snapshots_after_catalogue_changes()
    {
        await using var f = new LocalFixture();
        var owner = await f.SetupAsync();
        var service = Catalogue(f);
        var category = await service.SaveCategoryAsync(null, "පාන", "Drinks", true);
        var id = await service.SaveProductAsync(Tea(category));
        SaleId saleId;
        await using (var scope = f.Provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TillDbContext>();
            var p = await context.Products.IgnoreQueryFilters().SingleAsync();
            var terminal = await context.Terminals.IgnoreQueryFilters().SingleAsync();
            var sale = Sale.Start(p.ShopId, terminal.Id, ShiftId.New(), owner.Id, Currency.FromCode("LKR"), DateTimeOffset.UtcNow);
            sale.AddPricedLine(p, Quantity.Of(3), TaxTreatment.Exempt("NO-TAX"));
            sale.AddCashPayment(Money.FromMajor(255, sale.Currency), DateTimeOffset.UtcNow);
            sale.Complete(ReceiptNumber.Create("T1", 1), DateTimeOffset.UtcNow);
            context.Sales.Add(sale);
            await context.SaveChangesAsync();
            saleId = sale.Id;
        }
        await service.SaveProductAsync(Tea(category) with { Id = id, EnglishName = "Changed", ReceiptName = "නව තේ", NormalPrice = 200, Bands = [] });
        await f.RestartAsync();
        await using var read = f.Provider.CreateAsyncScope();
        var stored = await read.ServiceProvider.GetRequiredService<TillDbContext>().Sales.IgnoreQueryFilters()
            .Include(s => s.Lines).SingleAsync(s => s.Id == saleId);
        stored.Total.ToDecimal().ShouldBe(255);
        var line = stored.Lines.Single();
        line.AppliedPriceBand.ShouldBe(2);
        line.AppliedMinimumThousandths.ShouldBe(3000);
        line.ReceiptNameSnapshot.ShouldBe("තේ පැකට්");
        line.EnglishNameSnapshot.ShouldBe("Tea");
        line.UnitCost.ToDecimal().ShouldBe(50);
        Should.Throw<DomainException>(() => stored.ChangeLineQuantity(line.Id, Quantity.Of(2)));
    }

    [Fact]
    public async Task Legacy_catalogue_upgrade_preserves_data_but_requires_owner_translation_before_activation()
    {
        await using var f = new LocalFixture();
        var shop = Guid.NewGuid(); var category = Guid.NewGuid(); var product = Guid.NewGuid();
        await using (var scope = f.Provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TillDbContext>();
            await context.GetService<IMigrator>().MigrateAsync("20261109145345_LocalFoundation");
            await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Shops (Id,Name,LegalName,TimeZoneId,IsActive,CurrencyCode,CashIncrementMinor,PaperWidthMillimeters) VALUES ({shop},{"Existing"},{"Existing"},{"Asia/Colombo"},1,{"LKR"},100,80)");
            await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Categories (Id,ShopId,Name,DisplayOrder,IsActive) VALUES ({category},{shop},{"Tea"},0,1)");
            var tax = Guid.NewGuid();
            await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Products (Id,ShopId,Sku,Name,CategoryId,Unit,TaxRateId,IsOpenPrice,IsActive,UnitPriceMinor,UnitPriceCurrency,UnitCostMinor,UnitCostCurrency,ReorderLevelThousandths) VALUES ({product},{shop},{"LEGACY"},{"Original tea"},{category},{"Each"},{tax},0,1,10000,{"LKR"},5000,{"LKR"},0)");
        }
        await f.Transactions.InitializeAsync();
        await using var read = f.Provider.CreateAsyncScope();
        var db = read.ServiceProvider.GetRequiredService<TillDbContext>();
        var p = await db.Products.IgnoreQueryFilters().SingleAsync();
        p.Id.Value.ShouldBe(product);
        p.Name.ShouldBe("Original tea");
        p.UnitPrice.MinorUnits.ShouldBe(10000);
        p.IsActive.ShouldBeFalse();
        p.ReceiptName.ShouldBe("");
        p.SinhalaName.ShouldBe("");
        p.PrimaryBarcode.ShouldBeNull();
        p.PriceBands.ShouldBeEmpty();
    }
}
