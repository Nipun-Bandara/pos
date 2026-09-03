using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using RetailCore.Domain.Common;
using RetailCore.Domain.Monetary;
using RetailCore.Domain.Sales;

namespace RetailCore.Infrastructure.Persistence;

/// <summary>
/// How domain value types become columns.
/// </summary>
/// <remarks>
/// Kept in one place so that the storage shape of a value object is a single decision
/// rather than something rediscovered in each configuration file.
/// </remarks>
internal static class ValueConverters
{
    /// <summary>
    /// Identifiers store as GUIDs. SYN-02 makes them UUIDv7, so they stay sortable and
    /// index inserts stay sequential.
    /// </summary>
    public sealed class ShopIdConverter() : ValueConverter<ShopId, Guid>(v => v.Value, v => new ShopId(v));

    public sealed class TerminalIdConverter()
        : ValueConverter<TerminalId, Guid>(v => v.Value, v => new TerminalId(v));

    public sealed class UserIdConverter() : ValueConverter<UserId, Guid>(v => v.Value, v => new UserId(v));

    public sealed class ProductIdConverter()
        : ValueConverter<ProductId, Guid>(v => v.Value, v => new ProductId(v));

    public sealed class CategoryIdConverter()
        : ValueConverter<CategoryId, Guid>(v => v.Value, v => new CategoryId(v));

    public sealed class TaxRateIdConverter()
        : ValueConverter<TaxRateId, Guid>(v => v.Value, v => new TaxRateId(v));

    public sealed class SaleIdConverter() : ValueConverter<SaleId, Guid>(v => v.Value, v => new SaleId(v));

    public sealed class SaleLineIdConverter()
        : ValueConverter<SaleLineId, Guid>(v => v.Value, v => new SaleLineId(v));

    public sealed class PaymentIdConverter()
        : ValueConverter<PaymentId, Guid>(v => v.Value, v => new PaymentId(v));

    public sealed class RefundIdConverter() : ValueConverter<RefundId, Guid>(v => v.Value, v => new RefundId(v));

    public sealed class RefundLineIdConverter()
        : ValueConverter<RefundLineId, Guid>(v => v.Value, v => new RefundLineId(v));

    public sealed class ShiftIdConverter() : ValueConverter<ShiftId, Guid>(v => v.Value, v => new ShiftId(v));

    public sealed class CashMovementIdConverter()
        : ValueConverter<CashMovementId, Guid>(v => v.Value, v => new CashMovementId(v));

    public sealed class StockMovementIdConverter()
        : ValueConverter<StockMovementId, Guid>(v => v.Value, v => new StockMovementId(v));

    public sealed class AuditEntryIdConverter()
        : ValueConverter<AuditEntryId, Guid>(v => v.Value, v => new AuditEntryId(v));

    /// <summary>
    /// The discount threshold in force when the sale was rung up. SAL-09.
    /// </summary>
    public sealed class DiscountPolicyConverter()
        : ValueConverter<DiscountPolicy, decimal>(
            v => v.MaxUnauthorisedRate,
            v => DiscountPolicy.UpTo(v));

    /// <summary>
    /// The nullable receipt number, stored as its printed form. SYN-03.
    /// </summary>
    /// <remarks>
    /// Stored as text rather than split into two columns because it is looked up whole
    /// (BO-04, a customer holding a paper receipt) and never aggregated.
    /// </remarks>
    public sealed class ReceiptNumberConverter()
        : ValueConverter<ReceiptNumber, string>(v => v.ToString(), v => ReceiptNumber.Parse(v));
}
