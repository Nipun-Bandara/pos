using RetailCore.Domain.Catalogue;
using RetailCore.Domain.Common;
using RetailCore.Domain.Inventory;
using RetailCore.Domain.Monetary;
using RetailCore.Domain.Sales;
using RetailCore.Domain.Security;
using RetailCore.Domain.Shifts;
using RetailCore.Domain.Tax;
using RetailCore.Domain.Tenancy;

namespace RetailCore.Application.Abstractions;

/// <summary>
/// Reads and writes sale aggregates.
/// </summary>
/// <remarks>
/// The repository returns whole aggregates with their lines and payments loaded. Anything
/// that needs a projection for a screen or a report goes through a query service instead, so
/// that reporting cannot accidentally load and mutate a half-populated aggregate.
/// </remarks>
public interface ISaleRepository
{
    Task<Sale?> FindAsync(SaleId id, CancellationToken cancellationToken = default);

    /// <summary>BO-04: a customer hands over a paper receipt.</summary>
    Task<Sale?> FindByReceiptAsync(ReceiptNumber receiptNumber, CancellationToken cancellationToken = default);

    /// <summary>SAL-11: what is parked on this terminal.</summary>
    Task<IReadOnlyList<Sale>> HeldSalesAsync(TerminalId terminalId, CancellationToken cancellationToken = default);

    void Add(Sale sale);
}

public interface IRefundRepository
{
    Task<Refund?> FindAsync(RefundId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// What has already gone back against each line of a sale, so RET-03 can be enforced
    /// across separate visits.
    /// </summary>
    Task<IReadOnlyDictionary<SaleLineId, Quantity>> RefundedQuantitiesAsync(
        SaleId saleId,
        CancellationToken cancellationToken = default);

    void Add(Refund refund);
}

public interface IProductRepository
{
    Task<Product?> FindAsync(ProductId id, CancellationToken cancellationToken = default);

    /// <summary>SAL-01. Matches any of the product's barcodes. CAT-07.</summary>
    Task<Product?> FindByBarcodeAsync(string barcode, CancellationToken cancellationToken = default);

    Task<Product?> FindBySkuAsync(string sku, CancellationToken cancellationToken = default);

    /// <summary>SAL-02. Non-functional target: under 150ms over 10,000 items.</summary>
    Task<IReadOnlyList<Product>> SearchAsync(
        string term,
        int limit = 50,
        CancellationToken cancellationToken = default);

    void Add(Product product);
}

public interface ITaxRateRepository
{
    /// <summary>CMP-02: the rate effective on the date of the sale, not today's rate.</summary>
    Task<TaxRate?> EffectiveAsync(
        TaxRateId id,
        DateOnly asOf,
        CancellationToken cancellationToken = default);

    void Add(TaxRate taxRate);
}

public interface IShiftRepository
{
    Task<Shift?> FindAsync(ShiftId id, CancellationToken cancellationToken = default);

    /// <summary>CSH-02: at most one, enforced by a unique index rather than by this query.</summary>
    Task<Shift?> OpenShiftForAsync(TerminalId terminalId, CancellationToken cancellationToken = default);

    /// <summary>The figures a Z report needs, summed in the database. CSH-04.</summary>
    Task<ShiftSalesSummary> SalesSummaryAsync(ShiftId shiftId, CancellationToken cancellationToken = default);

    void Add(Shift shift);
}

public interface IStockLedger
{
    Task PostAsync(ShopId shop, string sourceType, Guid sourceId, IReadOnlyList<StockMovement> movements, CancellationToken cancellationToken = default);
    /// <summary>INV-02: derived from the movement ledger, never read from a column.</summary>
    Task<Quantity> OnHandAsync(ProductId productId, CancellationToken cancellationToken = default);

    /// <summary>Signed, because INV-09 allows stock to go negative while offline.</summary>
    Task<decimal> SignedOnHandAsync(ProductId productId, CancellationToken cancellationToken = default);

    void Add(StockMovement movement);

    void AddRange(IEnumerable<StockMovement> movements);
}

public interface IUserRepository
{
    Task<User?> FindAsync(UserId id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<User>> ActiveUsersAsync(CancellationToken cancellationToken = default);

    void Add(User user);
}

public interface IAuditLog
{
    /// <summary>SEC-06. Append only.</summary>
    void Record(AuditEntry entry);
}

/// <summary>Provisioning. Deliberately outside tenant filtering. TEN-05.</summary>
public interface IShopRepository
{
    Task<Shop?> FindAsync(ShopId id, CancellationToken cancellationToken = default);

    Task<Terminal?> FindTerminalAsync(TerminalId id, CancellationToken cancellationToken = default);

    void Add(Shop shop);

    void Add(Terminal terminal);
}
