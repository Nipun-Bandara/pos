using RetailCore.Domain.Catalogue;
using RetailCore.Domain.Common;
using RetailCore.Domain.Sales;

namespace RetailCore.Application.Checkout;

public sealed record BasketItem(ProductId ProductId, long QuantityThousandths, long? RequestedUnitPriceMinor = null, string? Reason = null);
public sealed record BasketDocument(Guid Id, long Version, IReadOnlyList<BasketItem> Items,
    CompletionRequest? LastRequest = null, CompletionResult? LastResult = null)
{
    public static BasketDocument Empty(CompletionRequest? lastRequest = null, CompletionResult? lastResult = null) =>
        new(Guid.CreateVersion7(), 0, [], lastRequest, lastResult);
}
public sealed record BasketEdit(Guid BasketId, long Version, ProductId ProductId, decimal Quantity,
    bool Remove = false, decimal? RequestedUnitPrice = null, string? Reason = null);
public sealed record CompletionRequest(Guid BasketId, long Version, string QuoteHash, PaymentMethod Method,
    long TenderedMinor, bool CardApproved = false, string? CardReference = null,
    bool AllowNegative = false, string? NegativeReason = null);
public sealed record CompletionResult(Guid SaleId, string Receipt, long TotalMinor, long RoundingMinor,
    long PaidMinor, long TenderedMinor, long ChangeMinor, string Currency, PaymentMethod Method);
public sealed record BillingProduct(ProductId Id, string Sku, string SinhalaName, string EnglishName,
    CategoryId CategoryId, UnitOfMeasure Unit, IReadOnlyList<string> Barcodes, bool Active);
public sealed record BillingCategory(CategoryId Id, string Display);
public sealed record BillingLine(ProductId ProductId, string Sku, string SinhalaName, string EnglishName, string ReceiptName,
    decimal Quantity, UnitOfMeasure Unit, decimal NormalUnitPrice, decimal TierUnitPrice, int TierBand,
    decimal ChargedUnitPrice, decimal Total, decimal Savings, bool Manual, string? Reason,
    long CostMinor, string TaxCode, string TaxCategory, decimal TaxRate, ProductId StockProduct,
    long StockFactorThousandths, UnitOfMeasure BaseUnit);
public sealed record BillingStock(ProductId ProductId, decimal Available, decimal Required, decimal Remaining, string Revision)
{
    public bool Insufficient => Remaining < 0;
}
public sealed record BillingQuote(string Hash, IReadOnlyList<BillingLine> Lines, IReadOnlyList<BillingStock> Stock,
    long TotalMinor, long CashRoundingMinor, long CashDueMinor, long CashIncrementMinor, string Currency,
    bool NeedsDiscountApproval, string? ErrorKey = null)
{
    public bool NeedsStockApproval => Stock.Any(s => s.Insufficient);
}
public sealed record BillingShift(ShiftId Id, decimal OpeningFloat, decimal ExpectedCash);
public sealed record BillingState(BasketDocument Basket, BillingQuote Quote, BillingShift? Shift,
    IReadOnlyList<BillingProduct> Products, IReadOnlyList<BillingCategory> Categories);
