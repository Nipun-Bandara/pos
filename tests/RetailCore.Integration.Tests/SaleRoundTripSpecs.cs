using Microsoft.EntityFrameworkCore;
using RetailCore.Domain.Common;
using RetailCore.Domain.Inventory;
using RetailCore.Domain.Monetary;
using RetailCore.Domain.Sales;
using RetailCore.Domain.Security;
using RetailCore.Infrastructure.Persistence;

namespace RetailCore.Integration.Tests;

/// <summary>
/// PLAN.md Phase 1 exit criterion: a sale created in code round-trips to both databases and
/// reads back with identical totals.
/// </summary>
/// <remarks>
/// Written once and run against both providers. If SQLite and PostgreSQL ever disagree about
/// what a sale is worth, the till and the server disagree about what the shop took, and the
/// whole offline-first design falls over.
/// </remarks>
[Collection(nameof(DatabaseCollection))]
public class SaleRoundTripSpecs(DatabaseFixture fixture)
{
    public static TheoryData<string> Providers => DatabaseFixture.ProviderNames;

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task A_mixed_basket_reads_back_with_the_same_totals(string provider)
    {
        var harness = fixture.For(provider);
        var shop = SampleShop.Create();

        SaleId saleId;
        Money expectedTotal;
        Money expectedTax;
        Money expectedDiscount;
        Money expectedPayable;

        await using (var context = harness.NewContext())
        {
            await SeedAsync(context, harness, shop);

            var sale = shop.RingUpMixedBasket();

            // PAY-05 then PAY-02: the card settles part, the cash settles the rounded rest.
            sale.AddPayment(PaymentMethod.Card, SampleShop.Rupees(1_000.00m), "AUTH-4471", SampleShop.Now);
            sale.AddCashPayment(SampleShop.Rupees(1_000.00m), SampleShop.Now);
            sale.Complete(ReceiptNumber.Create(shop.Terminal.Code, 1), SampleShop.Now);

            saleId = sale.Id;
            expectedTotal = sale.Total;
            expectedTax = sale.TaxTotal;
            expectedDiscount = sale.DiscountTotal;
            expectedPayable = sale.AmountPayable;

            context.Sales.Add(sale);
            await context.SaveChangesAsync();
        }

        await using (var context = harness.NewContext())
        {
            var reloaded = await context.Sales
                .Include(s => s.Lines)
                .Include(s => s.Payments)
                .SingleAsync(s => s.Id == saleId);

            reloaded.Total.ShouldBe(expectedTotal);
            reloaded.TaxTotal.ShouldBe(expectedTax);
            reloaded.DiscountTotal.ShouldBe(expectedDiscount);
            reloaded.AmountPayable.ShouldBe(expectedPayable);
            reloaded.BalanceDue.ShouldBe(Money.Zero(SampleShop.Currency));
            reloaded.Status.ShouldBe(SaleStatus.Completed);
            reloaded.ReceiptNumber!.Value.ToString().ShouldBe("T1-000001");

            // The stored totals must still equal the sum of the stored lines, or reporting
            // and the receipt would disagree.
            Money.Sum(reloaded.ActiveLines.Select(l => l.LineTotal), SampleShop.Currency)
                .ShouldBe(reloaded.Total);
        }
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Every_value_object_survives_the_round_trip(string provider)
    {
        var harness = fixture.For(provider);
        var shop = SampleShop.Create();

        SaleId saleId;

        await using (var context = harness.NewContext())
        {
            await SeedAsync(context, harness, shop);

            var sale = shop.RingUpMixedBasket();
            sale.SetCustomer("Priya", "0771234567");
            sale.AddCashPayment(SampleShop.Rupees(2_000.00m), SampleShop.Now);
            sale.Complete(ReceiptNumber.Create(shop.Terminal.Code, 1), SampleShop.Now);

            saleId = sale.Id;
            context.Sales.Add(sale);
            await context.SaveChangesAsync();
        }

        await using (var context = harness.NewContext())
        {
            var reloaded = await context.Sales
                .Include(s => s.Lines)
                .SingleAsync(s => s.Id == saleId);

            // SAL-05: a weight of 0.285 kg is still 0.285 kg.
            var weighed = reloaded.Lines.Single(l => l.Sku == "VEG-TOM");
            weighed.Quantity.ShouldBe(Quantity.Of(0.285m));
            weighed.UnitPrice.ShouldBe(SampleShop.Rupees(450.00m));

            // CAT-05 and CMP-02: the tax rule as it stood, copied onto the line.
            weighed.TaxTreatment.Code.ShouldBe("VAT18");
            weighed.TaxTreatment.Rate.ShouldBe(0.18m);

            // CMP-03: the exempt line came back exempt, not merely at a zero rate.
            var exempt = reloaded.Lines.Single(l => l.Sku == "GRN-RICE");
            exempt.TaxTreatment.Category.ShouldBe(Domain.Tax.TaxCategory.Exempt);
            exempt.TaxAmount.ShouldBe(Money.Zero(SampleShop.Currency));

            // SAL-07 with SAL-09: the discount, its reason, and who allowed it.
            var discounted = reloaded.Lines.Single(l => l.Sku == "TIN-001");
            discounted.LineDiscount.ShouldNotBeNull();
            discounted.LineDiscount.Reason.ShouldBe("Dented tin");
            discounted.LineDiscount.Percentage.ShouldBe(0.05m);

            // SAL-08: the apportioned share.
            Money.Sum(reloaded.ActiveLines.Select(l => l.ApportionedSaleDiscount), SampleShop.Currency)
                .ShouldBe(SampleShop.Rupees(50.00m));

            // SAL-14.
            reloaded.CustomerName.ShouldBe("Priya");

            // SAL-09: the threshold in force at the time.
            reloaded.DiscountPolicy.MaxUnauthorisedRate.ShouldBe(0.10m);
        }
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Completing_a_sale_posts_the_stock_movements_in_the_same_transaction(string provider)
    {
        // INV-03. The handler runs inside SaveChanges, so the sale and its ledger entries
        // are one commit.
        var harness = fixture.For(provider);
        var shop = SampleShop.Create();

        await using (var context = harness.NewContext())
        {
            await SeedAsync(context, harness, shop);

            var sale = Sale.Start(
                shop.Shop.Id, shop.Terminal.Id, shop.Shift.Id, shop.Cashier.Id,
                SampleShop.Currency, SampleShop.Now);

            sale.AddLine(shop.TinnedFish, Quantity.Of(4), shop.StandardVat);
            sale.AddCashPayment(SampleShop.Rupees(500.00m), SampleShop.Now);
            sale.Complete(ReceiptNumber.Create(shop.Terminal.Code, 1), SampleShop.Now);

            context.Sales.Add(sale);
            await context.SaveChangesAsync();
        }

        await using (var context = harness.NewContext())
        {
            var ledger = new StockLedger(context);

            // INV-02: derived from the ledger, not read from a column.
            var onHand = await ledger.SignedOnHandAsync(shop.TinnedFish.Id);

            // Stock must be available unless an owner explicitly approved a shortage.
            onHand.ShouldBe(96m);

            var movement = await context.StockMovements
                .SingleAsync(m => m.ProductId == shop.TinnedFish.Id && m.Kind == StockMovementKind.Sale);

            movement.Kind.ShouldBe(StockMovementKind.Sale);
            movement.Direction.ShouldBe(StockDirection.Out);
            movement.Quantity.ShouldBe(Quantity.Of(4));
            movement.SourceType.ShouldBe("Sale");
        }
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task A_discount_is_written_to_the_audit_log_by_the_same_commit(string provider)
    {
        // SEC-06.
        var harness = fixture.For(provider);
        var shop = SampleShop.Create();

        await using (var context = harness.NewContext())
        {
            await SeedAsync(context, harness, shop);

            var sale = shop.RingUpMixedBasket();
            sale.AddCashPayment(SampleShop.Rupees(2_000.00m), SampleShop.Now);
            sale.Complete(ReceiptNumber.Create(shop.Terminal.Code, 1), SampleShop.Now);

            context.Sales.Add(sale);
            await context.SaveChangesAsync();
        }

        await using (var context = harness.NewContext())
        {
            var entries = await context.AuditEntries
                .Where(a => a.Action == AuditAction.DiscountGiven)
                .ToListAsync();

            // One for the line discount, one for the whole-sale discount.
            entries.Count.ShouldBe(2);
            entries.ShouldAllBe(e => e.ShopId == shop.Shop.Id);
        }
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Receipt_numbers_are_gapless_and_per_terminal(string provider)
    {
        // SYN-03.
        var harness = fixture.For(provider);
        var shop = SampleShop.Create();

        await using (var context = harness.NewContext())
        {
            await SeedAsync(context, harness, shop);
        }

        var issued = new List<string>();

        for (var i = 0; i < 5; i++)
        {
            await using var context = harness.NewContext();
            var allocator = new ReceiptNumberAllocator(context);

            var sale = Sale.Start(
                shop.Shop.Id, shop.Terminal.Id, shop.Shift.Id, shop.Cashier.Id,
                SampleShop.Currency, SampleShop.Now);

            sale.AddLine(shop.TinnedFish, Quantity.One, shop.StandardVat);
            sale.AddCashPayment(SampleShop.Rupees(200.00m), SampleShop.Now);

            var number = await allocator.NextAsync(shop.Shop.Id, shop.Terminal.Id);

            sale.Complete(number, SampleShop.Now);
            context.Sales.Add(sale);
            await context.SaveChangesAsync();

            issued.Add(number.ToString());
        }

        issued.ShouldBe(["T1-000001", "T1-000002", "T1-000003", "T1-000004", "T1-000005"]);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task An_abandoned_basket_does_not_consume_a_receipt_number(string provider)
    {
        // SYN-03: a gap in the sequence is something an auditor expects explained.
        var harness = fixture.For(provider);
        var shop = SampleShop.Create();

        await using (var context = harness.NewContext())
        {
            await SeedAsync(context, harness, shop);

            var abandoned = Sale.Start(
                shop.Shop.Id, shop.Terminal.Id, shop.Shift.Id, shop.Cashier.Id,
                SampleShop.Currency, SampleShop.Now);
            abandoned.AddLine(shop.TinnedFish, Quantity.One, shop.StandardVat);
            abandoned.Cancel();

            context.Sales.Add(abandoned);
            await context.SaveChangesAsync();
        }

        await using (var context = harness.NewContext())
        {
            var allocator = new ReceiptNumberAllocator(context);

            var number = await allocator.NextAsync(shop.Shop.Id, shop.Terminal.Id);

            number.Sequence.ShouldBe(1);
        }
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task A_refund_reads_back_and_restores_the_stock(string provider)
    {
        // RET-01, RET-03 and RET-06.
        var harness = fixture.For(provider);
        var shop = SampleShop.Create();

        SaleId saleId;

        await using (var context = harness.NewContext())
        {
            await SeedAsync(context, harness, shop);

            var sale = Sale.Start(
                shop.Shop.Id, shop.Terminal.Id, shop.Shift.Id, shop.Cashier.Id,
                SampleShop.Currency, SampleShop.Now);
            sale.AddLine(shop.TinnedFish, Quantity.Of(3), shop.StandardVat);
            sale.AddCashPayment(SampleShop.Rupees(400.00m), SampleShop.Now);
            sale.Complete(ReceiptNumber.Create(shop.Terminal.Code, 1), SampleShop.Now);

            saleId = sale.Id;
            context.Sales.Add(sale);
            await context.SaveChangesAsync();
        }

        await using (var context = harness.NewContext())
        {
            var sales = new SaleRepository(context);
            var refunds = new RefundRepository(context);

            var original = await sales.FindAsync(saleId);
            original.ShouldNotBeNull();

            var alreadyRefunded = await refunds.RefundedQuantitiesAsync(saleId);
            alreadyRefunded.ShouldBeEmpty();

            var refund = Refund.Against(
                original,
                shop.Shift.Id,
                shop.Cashier.Id,
                "One tin was dented",
                shop.Manager.Id,
                PaymentMethod.Cash,
                SampleShop.Now,
                alreadyRefunded);

            refund.AddLine(original.Lines[0].Id, Quantity.One);
            refund.Complete(ReceiptNumber.Create(shop.Terminal.Code, 2), SampleShop.Now);

            refunds.Add(refund);
            await context.SaveChangesAsync();
        }

        await using (var context = harness.NewContext())
        {
            var refunds = new RefundRepository(context);

            // RET-03: the second visit knows what the first one gave back.
            var alreadyRefunded = await refunds.RefundedQuantitiesAsync(saleId);
            alreadyRefunded.Values.Single().ShouldBe(Quantity.One);

            // RET-06: 100 received, three sold, one saleable return.
            var ledger = new StockLedger(context);
            (await ledger.SignedOnHandAsync(shop.TinnedFish.Id)).ShouldBe(98m);
        }
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task A_product_is_found_by_any_of_its_barcodes(string provider)
    {
        // SAL-01 and CAT-07.
        var harness = fixture.For(provider);
        var shop = SampleShop.Create();

        await using (var context = harness.NewContext())
        {
            await SeedAsync(context, harness, shop);

            var product = await context.Products
                .SingleAsync(p => p.Id == shop.TinnedFish.Id);

            product.AddBarcode("4791234567906");
            await context.SaveChangesAsync();
        }

        await using (var context = harness.NewContext())
        {
            var products = new ProductRepository(context);

            (await products.FindByBarcodeAsync("4791234567890"))
                .ShouldNotBeNull().Sku.ShouldBe("TIN-001");
            (await products.FindByBarcodeAsync("4791234567906"))
                .ShouldNotBeNull().Sku.ShouldBe("TIN-001");
            (await products.FindByBarcodeAsync("0000000000000"))
                .ShouldBeNull();
        }
    }

    /// <summary>
    /// Writes the shop and its reference data.
    /// </summary>
    /// <remarks>
    /// Provisioning runs with tenant filtering suspended, because the shop row is what
    /// establishes the tenant in the first place. TEN-04 proves this escape hatch does not
    /// weaken row level security.
    /// </remarks>
    internal static async Task SeedAsync(
        RetailCoreDbContext context,
        IDatabaseHarness harness,
        SampleShop shop)
    {
        harness.Tenant.SetShop(shop.Shop.Id);

        context.Shops.Add(shop.Shop);
        context.Terminals.Add(shop.Terminal);
        context.Users.AddRange(shop.Cashier, shop.Manager);
        context.Categories.Add(shop.Category);
        context.TaxRates.Add(shop.StandardRate);
        context.Products.AddRange(shop.TinnedFish, shop.Tomatoes, shop.Rice);
        context.Shifts.Add(shop.Shift);
        foreach (var product in new[] { shop.TinnedFish, shop.Tomatoes, shop.Rice })
            context.StockMovements.Add(StockMovement.Manual(shop.Shop.Id, product.Id, Quantity.Of(100), StockDirection.In,
                StockMovementKind.OpeningBalance, "Test opening", shop.Manager.Id, SampleShop.Now));

        await context.SaveChangesAsync();
    }
}
