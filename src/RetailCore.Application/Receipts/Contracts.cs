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
    long TotalMinor, long RoundingMinor, long PaidMinor, long ChangeMinor, bool LegacyHeader,
    string Kind = "Sale", string? OriginalNumber = null, string? ReplacementNumber = null, long ReturnCreditMinor = 0,
    long CashPayoutMinor = 0, long CardPayoutMinor = 0, string? Reason = null,
    IReadOnlyList<RetailCore.Application.Returns.ReceiptReturnLine>? Returned = null)
{
    public static string TemplateFor(int version) => version switch
    {
        1 => "provisional-si-v1", 2 => "owner-sample-si-v2", 3 => "return-exchange-si-v3",
        _ => throw new InvalidOperationException("Unsupported receipt snapshot version."),
    };
    public static ReceiptDocument ForReturn(Guid id,string number,Sale original,Shop shop,string actor,DateTimeOffset at,
        Refund refund,Sale? replacement,long collected,long rounding,long tendered,long change)
    {
        var header=Capture(original,shop,actor);
        var local=TimeZoneInfo.ConvertTime(at,TimeZoneInfo.FindSystemTimeZoneById(shop.TimeZoneId));
        return header with { Version=3,SaleId=id,Number=number,CompletedAt=at,LocalTime=local.ToString("yyyy-MM-dd HH:mm:ss zzz",System.Globalization.CultureInfo.InvariantCulture),
            Lines=replacement is null?[]:Capture(replacement,shop,actor).Lines,Payments=replacement is null?[]:Capture(replacement,shop,actor).Payments,
            GrossMinor=replacement?.GrossAmount.MinorUnits??0,NetMinor=replacement?.NetTotal.MinorUnits??0,TaxMinor=replacement?.TaxTotal.MinorUnits??0,
            TotalMinor=replacement?.Total.MinorUnits??0,DiscountMinor=replacement?.DiscountTotal.MinorUnits??0,
            RoundingMinor=rounding,PaidMinor=collected,ChangeMinor=change,Kind=refund.IsVoidReversal?"Void":replacement is null?"Return":"Exchange",
            OriginalNumber=original.ReceiptNumber!.Value.ToString(),ReplacementNumber=replacement?.ReceiptNumber?.ToString(),ReturnCreditMinor=refund.Total.MinorUnits,
            CashPayoutMinor=refund.CashPaidMinor??0,CardPayoutMinor=refund.CardPaidMinor??0,Reason=refund.Reason,
            Returned=refund.Lines.Select(l=>new RetailCore.Application.Returns.ReceiptReturnLine(original.Lines.Single(o=>o.Id==l.OriginalSaleLineId).ReceiptNameSnapshot??l.Description,
                l.Quantity.Thousandths,ReceiptLayout.Label("Unit"+original.Lines.Single(o=>o.Id==l.OriginalSaleLineId).Unit),l.Amount.MinorUnits,l.Disposition==ReturnDisposition.Saleable)).ToArray() };
    }
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
public sealed record BillDetail(ReceiptDocument Receipt, SaleStatus Status, IReadOnlyList<PrintHistory> Prints, IReadOnlyList<RetailCore.Application.Returns.ReturnResult>? Returns = null);
public sealed record ReturnPrintInfo(ReceiptDocument Document,IReadOnlyList<PrintHistory> Prints);
public sealed record PrintPayload(ReceiptDocument Receipt, PrintAttempt Attempt, SaleStatus Status);
public sealed record ReceiptSettings(int PrintableWidthMillimeters, string? Printer, DateOnly Today);
public sealed record PrintResult(Guid AttemptId, string Status);
public interface IReceiptPrinter
{
    /// <summary>Returns when accepted by the Windows spooler, not proof of physical paper output.</summary>
    Task SendAsync(PrintPayload payload, CancellationToken cancellationToken);
}
