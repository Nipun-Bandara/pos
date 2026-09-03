using Microsoft.EntityFrameworkCore;
using RetailCore.Application.Abstractions;
using RetailCore.Domain.Catalogue;
using RetailCore.Domain.Common;
using RetailCore.Domain.Inventory;
using RetailCore.Domain.Monetary;
using RetailCore.Domain.Sales;
using RetailCore.Domain.Security;
using RetailCore.Domain.Shifts;
using RetailCore.Domain.Tax;
using RetailCore.Domain.Tenancy;

namespace RetailCore.Infrastructure.Persistence;

/// <summary>The transaction boundary, delegating to the context. </summary>
public sealed class UnitOfWork(RetailCoreDbContext context) : IUnitOfWork
{
    public Task<int> CommitAsync(CancellationToken cancellationToken = default) =>
        context.SaveChangesAsync(cancellationToken);
}

public sealed class SaleRepository(RetailCoreDbContext context) : ISaleRepository
{
    /// <summary>
    /// Loads the whole aggregate. The lines and payments are not optional extras: the sale's
    /// invariants are stated in terms of them, so a partially loaded sale could compute a
    /// wrong total and complete on a short payment.
    /// </summary>
    public Task<Sale?> FindAsync(SaleId id, CancellationToken cancellationToken = default) =>
        Aggregate().FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public Task<Sale?> FindByReceiptAsync(
        ReceiptNumber receiptNumber,
        CancellationToken cancellationToken = default) =>
        Aggregate().FirstOrDefaultAsync(s => s.ReceiptNumber == receiptNumber, cancellationToken);

    public async Task<IReadOnlyList<Sale>> HeldSalesAsync(
        TerminalId terminalId,
        CancellationToken cancellationToken = default) =>
        await Aggregate()
            .Where(s => s.TerminalId == terminalId && s.Status == SaleStatus.Held)
            .OrderBy(s => s.StartedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public void Add(Sale sale) => context.Sales.Add(sale);

    private IQueryable<Sale> Aggregate() =>
        context.Sales
            .Include(s => s.Lines)
            .Include(s => s.Payments);
}

public sealed class RefundRepository(RetailCoreDbContext context) : IRefundRepository
{
    public Task<Refund?> FindAsync(RefundId id, CancellationToken cancellationToken = default) =>
        context.Refunds.Include(r => r.Lines).FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    /// <summary>
    /// What has already gone back against each line. RET-03 depends on this being complete,
    /// so it counts every refund against the sale, not just the recent ones.
    /// </summary>
    public async Task<IReadOnlyDictionary<SaleLineId, Quantity>> RefundedQuantitiesAsync(
        SaleId saleId,
        CancellationToken cancellationToken = default)
    {
        var rows = await context.Refunds
            .Where(r => r.OriginalSaleId == saleId && r.CompletedAt != null)
            .SelectMany(r => r.Lines)
            .GroupBy(l => l.OriginalSaleLineId)
            .Select(g => new { LineId = g.Key, Thousandths = g.Sum(l => l.Quantity.Thousandths) })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.ToDictionary(r => r.LineId, r => Quantity.FromThousandths(r.Thousandths));
    }

    public void Add(Refund refund) => context.Refunds.Add(refund);
}

public sealed class ProductRepository(RetailCoreDbContext context) : IProductRepository
{
    public Task<Product?> FindAsync(ProductId id, CancellationToken cancellationToken = default) =>
        context.Products.Include(p => p.Barcodes).FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    /// <summary>SAL-01. An index seek on the barcode table, inside the 100ms scan budget.</summary>
    public Task<Product?> FindByBarcodeAsync(
        string barcode,
        CancellationToken cancellationToken = default) =>
        context.Products
            .Include(p => p.Barcodes)
            .FirstOrDefaultAsync(p => p.Barcodes.Any(b => b.Code == barcode), cancellationToken);

    public Task<Product?> FindBySkuAsync(string sku, CancellationToken cancellationToken = default) =>
        context.Products.Include(p => p.Barcodes).FirstOrDefaultAsync(p => p.Sku == sku, cancellationToken);

    public async Task<IReadOnlyList<Product>> SearchAsync(
        string term,
        int limit = 50,
        CancellationToken cancellationToken = default) =>
        await context.Products
            .Where(p => p.IsActive && (EF.Functions.Like(p.Name, $"%{term}%") || p.Sku == term))
            .OrderBy(p => p.Name)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public void Add(Product product) => context.Products.Add(product);
}

public sealed class TaxRateRepository(RetailCoreDbContext context) : ITaxRateRepository
{
    /// <summary>CMP-02: the rate effective on the date of the sale, not today's rate.</summary>
    public Task<TaxRate?> EffectiveAsync(
        TaxRateId id,
        DateOnly asOf,
        CancellationToken cancellationToken = default) =>
        context.TaxRates.FirstOrDefaultAsync(
            t => t.Id == id
                 && t.EffectiveFrom <= asOf
                 && (t.EffectiveTo == null || t.EffectiveTo >= asOf),
            cancellationToken);

    public void Add(TaxRate taxRate) => context.TaxRates.Add(taxRate);
}

public sealed class ShiftRepository(RetailCoreDbContext context) : IShiftRepository
{
    public Task<Shift?> FindAsync(ShiftId id, CancellationToken cancellationToken = default) =>
        context.Shifts.Include(s => s.CashMovements).FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public Task<Shift?> OpenShiftForAsync(
        TerminalId terminalId,
        CancellationToken cancellationToken = default) =>
        context.Shifts
            .Include(s => s.CashMovements)
            .FirstOrDefaultAsync(
                s => s.TerminalId == terminalId && s.Status == ShiftStatus.Open,
                cancellationToken);

    /// <summary>
    /// The figures the X and Z reports need. CSH-03, CSH-04.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Summed in the database over the stored minor units, which is what the two-column money
    /// mapping buys. Cash and non-cash are separated because only cash reaches the drawer, and
    /// a Z report that includes card takings in the expected drawer figure is unexplainable.
    /// </para>
    /// <para>
    /// Voided sales are excluded, since the money went back to the customer, and the void
    /// already restored the stock.
    /// </para>
    /// </remarks>
    public async Task<ShiftSalesSummary> SalesSummaryAsync(
        ShiftId shiftId,
        CancellationToken cancellationToken = default)
    {
        var shift = await context.Shifts
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == shiftId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Shift {shiftId} was not found.");

        var currency = shift.Currency;

        var completedSales = context.Sales
            .Where(s => s.ShiftId == shiftId && s.Status == SaleStatus.Completed);

        var tenders = await completedSales
            .SelectMany(s => s.Payments)
            .GroupBy(p => p.Method)
            .Select(g => new { Method = g.Key, Minor = g.Sum(p => p.Amount.MinorUnits) })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Change handed back reduces the cash in the drawer, so it comes off the cash taken.
        var changeGiven = await completedSales
            .SelectMany(s => s.Payments)
            .Where(p => p.Method == PaymentMethod.Cash)
            .SumAsync(p => p.Tendered.MinorUnits - p.Amount.MinorUnits, cancellationToken)
            .ConfigureAwait(false);

        var cashSales = tenders.Where(t => t.Method == PaymentMethod.Cash).Sum(t => t.Minor) - changeGiven;
        var nonCashSales = tenders.Where(t => t.Method != PaymentMethod.Cash).Sum(t => t.Minor);

        var refunds = await context.Refunds
            .Where(r => r.ShiftId == shiftId && r.CompletedAt != null)
            .GroupBy(r => r.Method)
            .Select(g => new { Method = g.Key, Minor = g.Sum(r => r.Total.MinorUnits) })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var totals = await completedSales
            .Select(s => new
            {
                s.TaxTotal.MinorUnits,
                Discount = s.DiscountTotal.MinorUnits,
                Rounding = s.CashRoundingAdjustment.MinorUnits,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var saleCount = await completedSales.CountAsync(cancellationToken).ConfigureAwait(false);
        var refundCount = await context.Refunds
            .CountAsync(r => r.ShiftId == shiftId && r.CompletedAt != null, cancellationToken)
            .ConfigureAwait(false);

        return new ShiftSalesSummary(
            Money.FromMinor(cashSales, currency),
            Money.FromMinor(refunds.Where(r => r.Method == PaymentMethod.Cash).Sum(r => r.Minor), currency),
            Money.FromMinor(nonCashSales, currency),
            Money.FromMinor(refunds.Where(r => r.Method != PaymentMethod.Cash).Sum(r => r.Minor), currency),
            saleCount,
            refundCount,
            Money.FromMinor(totals.Sum(t => t.MinorUnits), currency),
            Money.FromMinor(totals.Sum(t => t.Discount), currency),
            Money.FromMinor(totals.Sum(t => t.Rounding), currency));
    }

    public void Add(Shift shift) => context.Shifts.Add(shift);
}

public sealed class StockLedger(RetailCoreDbContext context) : IStockLedger
{
    /// <summary>
    /// INV-02: derived from the ledger, never read from a column. Clamped at zero because a
    /// caller asking "how many do I have" cannot use a negative answer; use
    /// <see cref="SignedOnHandAsync"/> when the sign matters.
    /// </summary>
    public async Task<Quantity> OnHandAsync(
        ProductId productId,
        CancellationToken cancellationToken = default)
    {
        var signed = await SignedOnHandAsync(productId, cancellationToken).ConfigureAwait(false);
        return signed <= 0m ? Quantity.Zero : Quantity.Rounded(signed);
    }

    /// <summary>The ledger balance in thousandths, summed in the database. INV-02.</summary>
    private Task<long> SignedThousandthsAsync(ProductId productId, CancellationToken cancellationToken) =>
        context.StockMovements
            .Where(m => m.ProductId == productId)
            .SumAsync(
                m => m.Direction == StockDirection.In ? m.Quantity.Thousandths : -m.Quantity.Thousandths,
                cancellationToken);

    /// <summary>INV-09: negative is a real answer while a till has been offline.</summary>
    public async Task<decimal> SignedOnHandAsync(
        ProductId productId,
        CancellationToken cancellationToken = default) =>
        await SignedThousandthsAsync(productId, cancellationToken).ConfigureAwait(false) / 1000m;

    public void Add(StockMovement movement) => context.StockMovements.Add(movement);

    public void AddRange(IEnumerable<StockMovement> movements) => context.StockMovements.AddRange(movements);
}

public sealed class UserRepository(RetailCoreDbContext context) : IUserRepository
{
    public Task<User?> FindAsync(UserId id, CancellationToken cancellationToken = default) =>
        context.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

    public async Task<IReadOnlyList<User>> ActiveUsersAsync(CancellationToken cancellationToken = default) =>
        await context.Users
            .Where(u => u.IsActive)
            .OrderBy(u => u.DisplayName)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public void Add(User user) => context.Users.Add(user);
}

/// <summary>SEC-06. Append only: there is no update or delete here.</summary>
public sealed class AuditLog(RetailCoreDbContext context) : IAuditLog
{
    public void Record(AuditEntry entry) => context.AuditEntries.Add(entry);
}

/// <summary>
/// Provisioning. TEN-05.
/// </summary>
/// <remarks>
/// Shops and terminals sit outside tenant filtering by nature: the shop table is the tenant
/// list, and a terminal has to be found before its shop is known.
/// </remarks>
public sealed class ShopRepository(RetailCoreDbContext context) : IShopRepository
{
    public Task<Shop?> FindAsync(ShopId id, CancellationToken cancellationToken = default) =>
        context.Shops.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public Task<Terminal?> FindTerminalAsync(
        TerminalId id,
        CancellationToken cancellationToken = default) =>
        context.Terminals.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public void Add(Shop shop) => context.Shops.Add(shop);

    public void Add(Terminal terminal) => context.Terminals.Add(terminal);
}
