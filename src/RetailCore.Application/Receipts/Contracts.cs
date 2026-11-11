using RetailCore.Domain.Catalogue;
using RetailCore.Domain.Sales;
using RetailCore.Domain.Tenancy;

namespace RetailCore.Application.Receipts;

public sealed record ReceiptLine(int Number, string Sku, string Name, string? SinhalaName, string? EnglishName,
    long QuantityThousandths, UnitOfMeasure Unit, long UnitPriceMinor, long CostMinor, long DiscountMinor,
    long TotalMinor, long TaxMinor, string TaxCode, string TaxCategory, decimal TaxRate,
    long? NormalPriceMinor, long? TierPriceMinor, int? TierBand, string? ManualReason);
public sealed record ReceiptPayment(PaymentMethod Method, long PaidMinor, long TenderedMinor, long ChangeMinor);
public sealed record ReceiptDocument(int Version, Guid SaleId, string Number, DateTimeOffset CompletedAt,
    string LocalTime, string TimeZone, string ShopName, string? Address, string? Telephone, string? TaxRegistration,
    string? Footer, string Cashier, string Currency, IReadOnlyList<ReceiptLine> Lines,
    IReadOnlyList<ReceiptPayment> Payments, long GrossMinor, long DiscountMinor, long NetMinor, long TaxMinor,
    long TotalMinor, long RoundingMinor, long PaidMinor, long ChangeMinor, bool LegacyHeader)
{
    public static string TemplateFor(int version) => version switch
    {
        1 => "provisional-si-v1", 2 => "owner-sample-si-v2",
        _ => throw new InvalidOperationException("Unsupported receipt snapshot version."),
    };
    public static ReceiptDocument Capture(Sale sale, Shop shop, string cashier, bool legacy = false)
    {
        if (sale.ReceiptNumber is null || sale.CompletedAt is null || sale.IsEditable)
            throw new InvalidOperationException("Only a saved completed bill can have a receipt.");
        var local = TimeZoneInfo.ConvertTime(sale.CompletedAt.Value, TimeZoneInfo.FindSystemTimeZoneById(shop.TimeZoneId));
        return new(legacy ? 1 : 2, sale.Id.Value, sale.ReceiptNumber.Value.ToString(), sale.CompletedAt.Value,
            local.ToString("yyyy-MM-dd HH:mm:ss zzz", System.Globalization.CultureInfo.InvariantCulture), shop.TimeZoneId,
            shop.Name, string.Join("\n", new[] { shop.AddressLine1, shop.AddressLine2 }.Where(s => !string.IsNullOrWhiteSpace(s))),
            shop.Telephone, shop.TaxRegistrationNumber, shop.ReceiptFooter, cashier, sale.CurrencyCode,
            sale.Lines.Where(l => !l.IsVoided).OrderBy(l => l.LineNumber).Select(l => new ReceiptLine(l.LineNumber, l.Sku,
                l.ReceiptNameSnapshot ?? l.SinhalaNameSnapshot ?? l.Description, l.SinhalaNameSnapshot, l.EnglishNameSnapshot,
                l.Quantity.Thousandths, l.Unit, l.UnitPrice.MinorUnits, l.UnitCost.MinorUnits, l.TotalDiscountAmount.MinorUnits,
                l.LineTotal.MinorUnits, l.TaxAmount.MinorUnits, l.TaxTreatment.Code, l.TaxTreatment.Category.ToString(), l.TaxTreatment.Rate,
                l.NormalUnitPriceMinor, l.QualifiedTierUnitPriceMinor, l.QualifiedPriceBand, l.ManualPriceReason)).ToArray(),
            sale.Payments.Select(p => new ReceiptPayment(p.Method, p.Amount.MinorUnits, p.Tendered.MinorUnits, p.Change.MinorUnits)).ToArray(),
            sale.GrossAmount.MinorUnits, sale.DiscountTotal.MinorUnits, sale.NetTotal.MinorUnits, sale.TaxTotal.MinorUnits,
            sale.Total.MinorUnits, sale.CashRoundingAdjustment.MinorUnits, sale.AmountSettled.MinorUnits, sale.ChangeGiven.MinorUnits, legacy);
    }
}
public sealed record BillSummary(Guid SaleId, string Number, DateTimeOffset CompletedAt, string LocalTime, long TotalMinor, string Currency, SaleStatus Status)
{ public string Total => ReceiptLayout.Amount(TotalMinor, Currency); }
public sealed record BillFilter(DateOnly From, DateOnly Through, string? Number = null);
public sealed record PrintAttempt(Guid Id, Guid SaleId, Guid ActorId, DateTimeOffset RequestedAt, string Printer,
    int PrintableWidthMillimeters, bool IsCopy, string DocumentHash);
public sealed record PrintOutcome(Guid AttemptId, DateTimeOffset At, string Status);
public sealed record PrintHistory(PrintAttempt Attempt, PrintOutcome? Outcome);
public sealed record BillDetail(ReceiptDocument Receipt, SaleStatus Status, IReadOnlyList<PrintHistory> Prints);
public sealed record PrintPayload(ReceiptDocument Receipt, PrintAttempt Attempt, SaleStatus Status);
public sealed record ReceiptSettings(int PrintableWidthMillimeters, string? Printer, DateOnly Today);
public sealed record PrintResult(Guid AttemptId, string Status);
public interface IReceiptPrinter
{
    /// <summary>Returns when accepted by the Windows spooler, not proof of physical paper output.</summary>
    Task SendAsync(PrintPayload payload, CancellationToken cancellationToken);
}
