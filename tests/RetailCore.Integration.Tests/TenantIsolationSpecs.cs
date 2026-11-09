using Microsoft.EntityFrameworkCore;
using RetailCore.Domain.Monetary;
using RetailCore.Domain.Sales;

namespace RetailCore.Integration.Tests;

/// <summary>
/// Two shops on one server, and neither can see the other. TEN-02, TEN-03, TEN-04.
/// </summary>
/// <remarks>
/// PLAN.md lists "two shops on one server, one cannot query the other, proven by test" as a
/// proof point that derisks the project. This is that test. The version that matters is
/// <see cref="Row_level_security_holds_even_with_the_application_filter_disabled"/>: anything
/// can forget an EF query filter, and the database policy is what stands behind it.
/// </remarks>
[Collection(nameof(DatabaseCollection))]
public class TenantIsolationSpecs(DatabaseFixture fixture)
{
    public static TheoryData<string> Providers => DatabaseFixture.ProviderNames;

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task A_shop_sees_only_its_own_sales(string provider)
    {
        // TEN-03.
        var harness = fixture.For(provider);
        var (first, second) = await SeedTwoShopsAsync(harness);

        await using (var context = harness.NewContext())
        {
            harness.Tenant.SetShop(first.Shop.Id);

            var sales = await context.Sales.ToListAsync();

            sales.ShouldHaveSingleItem();
            sales[0].ShopId.ShouldBe(first.Shop.Id);
        }

        await using (var context = harness.NewContext())
        {
            harness.Tenant.SetShop(second.Shop.Id);

            var sales = await context.Sales.ToListAsync();

            sales.ShouldHaveSingleItem();
            sales[0].ShopId.ShouldBe(second.Shop.Id);
        }
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task A_shop_cannot_read_another_shops_catalogue_or_ledger(string provider)
    {
        var harness = fixture.For(provider);
        var (first, second) = await SeedTwoShopsAsync(harness);

        await using var context = harness.NewContext();
        harness.Tenant.SetShop(first.Shop.Id);

        (await context.Products.CountAsync(p => p.ShopId == second.Shop.Id)).ShouldBe(0);
        (await context.Users.CountAsync(u => u.ShopId == second.Shop.Id)).ShouldBe(0);
        (await context.StockMovements.CountAsync(m => m.ShopId == second.Shop.Id)).ShouldBe(0);
        (await context.AuditEntries.CountAsync(a => a.ShopId == second.Shop.Id)).ShouldBe(0);

        // Child tables carry their own ShopId precisely so this question can be asked of them.
        (await context.Set<SaleLine>().CountAsync(l => l.ShopId == second.Shop.Id)).ShouldBe(0);
        (await context.Set<Payment>().CountAsync(p => p.ShopId == second.Shop.Id)).ShouldBe(0);
    }

    [Fact(Skip = "Legacy PostgreSQL checks require RETAILCORE_TEST_POSTGRES=1.",
        SkipType = typeof(DatabaseFixture), SkipUnless = nameof(DatabaseFixture.PostgresEnabled))]
    public async Task Row_level_security_holds_even_with_the_application_filter_disabled()
    {
        // TEN-04, and the reason PLAN.md calls row level security the primary control.
        // IgnoreQueryFilters turns off everything EF is doing. What is left is the database
        // policy, and it still has to hold.
        var harness = fixture.Postgres;
        var (first, second) = await SeedTwoShopsAsync(harness);

        await using var context = harness.NewContext();
        harness.Tenant.SetShop(first.Shop.Id);

        var everySaleTheDatabaseWillShow = await context.Sales
            .IgnoreQueryFilters()
            .ToListAsync();

        everySaleTheDatabaseWillShow.ShouldHaveSingleItem();
        everySaleTheDatabaseWillShow[0].ShopId.ShouldBe(first.Shop.Id);
        everySaleTheDatabaseWillShow.ShouldNotContain(s => s.ShopId == second.Shop.Id);

        var everyProduct = await context.Products.IgnoreQueryFilters().ToListAsync();
        everyProduct.ShouldAllBe(p => p.ShopId == first.Shop.Id);
    }

    [Fact(Skip = "Legacy PostgreSQL checks require RETAILCORE_TEST_POSTGRES=1.",
        SkipType = typeof(DatabaseFixture), SkipUnless = nameof(DatabaseFixture.PostgresEnabled))]
    public async Task A_connection_that_has_not_named_a_shop_sees_nothing()
    {
        // The policies fail closed. A connection that never issued SET LOCAL app.shop_id gets
        // an empty result rather than everything, which is the only safe default.
        var harness = fixture.Postgres;
        await SeedTwoShopsAsync(harness);

        await using var context = harness.NewContextWithoutTenant();

        var sales = await context.Sales.IgnoreQueryFilters().ToListAsync();

        sales.ShouldBeEmpty();
    }

    [Fact(Skip = "Legacy PostgreSQL checks require RETAILCORE_TEST_POSTGRES=1.",
        SkipType = typeof(DatabaseFixture), SkipUnless = nameof(DatabaseFixture.PostgresEnabled))]
    public async Task A_row_cannot_be_written_into_another_shop()
    {
        // The policy's WITH CHECK clause. Reading is not the only way to cross a tenant
        // boundary; writing into someone else's shop is just as bad and is blocked too.
        var harness = fixture.Postgres;
        var (first, second) = await SeedTwoShopsAsync(harness);

        await using var context = harness.NewContext();
        harness.Tenant.SetShop(first.Shop.Id);

        var trespassing = Domain.Catalogue.Product.Create(
            second.Shop.Id,
            "SMUGGLED",
            "Planted in another shop",
            second.Category.Id,
            Domain.Catalogue.UnitOfMeasure.Each,
            SampleShop.Rupees(1.00m),
            SampleShop.Rupees(1.00m),
            second.StandardRate.Id);

        context.Products.Add(trespassing);

        await Should.ThrowAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    /// <summary>
    /// Two shops, each with a completed sale, provisioned with filtering suspended.
    /// </summary>
    private static async Task<(SampleShop First, SampleShop Second)> SeedTwoShopsAsync(
        IDatabaseHarness harness)
    {
        var first = SampleShop.Create("Sunrise Minimart", $"A{Random.Shared.Next(1000, 9999)}");
        var second = SampleShop.Create("Lakeside Stores", $"B{Random.Shared.Next(1000, 9999)}");

        foreach (var shop in new[] { first, second })
        {
            await using var context = harness.NewContext();
            await SaleRoundTripSpecs.SeedAsync(context, harness, shop);

            var sale = Sale.Start(
                shop.Shop.Id, shop.Terminal.Id, shop.Shift.Id, shop.Cashier.Id,
                SampleShop.Currency, SampleShop.Now);

            sale.AddLine(shop.TinnedFish, Quantity.One, shop.StandardVat);
            sale.AddCashPayment(SampleShop.Rupees(200.00m), SampleShop.Now);
            sale.Complete(ReceiptNumber.Create(shop.Terminal.Code, 1), SampleShop.Now);

            context.Sales.Add(sale);
            await context.SaveChangesAsync();
        }

        return (first, second);
    }
}
