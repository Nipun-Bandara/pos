using System.Globalization;
using System.Resources;
using RetailCore.Domain.Monetary;

namespace RetailCore.Application.Receipts;

public enum ReceiptStyle { Plain, Title, Center, Rule, Pair, ColumnsHeader, ItemName, Columns, Box }
public sealed record ReceiptRow(string Text, bool Emphasis = false, ReceiptStyle Style = ReceiptStyle.Plain,
    string? Column1 = null, string? Column2 = null, string? Column3 = null, string? Column4 = null);
/// <summary>Fixed provisional layout. All labels resolve from Sinhala resources regardless of UI culture.</summary>
public static class ReceiptLayout
{
    private static readonly ResourceManager Labels = new("RetailCore.Application.Receipts.ReceiptLabels", typeof(ReceiptLayout).Assembly);
    public static string Label(string key) => Labels.GetString(key, CultureInfo.InvariantCulture) ?? throw new InvalidOperationException(key);
    public static string Amount(long minor, string currency)
    {
        var c = Currency.FromCode(currency);
        return Money.FromMinor(minor, c).ToDecimal().ToString("F" + c.MinorUnitDigits, CultureInfo.InvariantCulture);
    }
    public static IReadOnlyList<ReceiptRow> Build(ReceiptDocument d, bool copy, RetailCore.Domain.Sales.SaleStatus status = RetailCore.Domain.Sales.SaleStatus.Completed)
    {
        if (d.Version == 3) return ReturnReceiptLayout.Build(d,copy);
        if (d.Version == 2) return SampleReceiptLayout.Build(d, copy, status);
        if (d.Version != 1) throw new InvalidOperationException("Unsupported receipt snapshot version.");
        string A(long minor) => Amount(minor, d.Currency);
        var rows = new List<ReceiptRow> { new(Label("Provisional"), true), new(d.ShopName, true) };
        void Add(string? text, bool bold = false) { if (!string.IsNullOrWhiteSpace(text)) rows.Add(new(text, bold)); }
        Add(d.Address); Add(d.Telephone); if (!string.IsNullOrWhiteSpace(d.TaxRegistration)) Add(Label("Registration") + ": " + d.TaxRegistration);
        Add(Label(copy ? "Copy" : "Original"), true);
        if (status == RetailCore.Domain.Sales.SaleStatus.Voided) Add(Label("Voided"), true); Add(Label("Bill") + ": " + d.Number, true);
        Add(Label("Date") + ": " + d.LocalTime); Add(Label("Cashier") + ": " + d.Cashier);
        if (d.LegacyHeader) Add(Label("Legacy"), true);
        foreach (var l in d.Lines)
        {
            Add(l.Number + ". " + l.Name, true);
            Add((l.QuantityThousandths / 1000m).ToString("0.###", CultureInfo.InvariantCulture) + " " + Label("Unit" + l.Unit)
                + " × " + A(l.UnitPriceMinor) + " = " + A(l.TotalMinor));
            if (l.DiscountMinor != 0) Add(Label("Discount") + ": " + A(l.DiscountMinor));
            if (l.TierBand is > 0) Add(Label("Tier") + ": " + (l.TierBand + 1));
            if (l.ManualReason is not null) Add(Label("Manual"));
        }
        Add(Label("Gross") + ": " + A(d.GrossMinor)); Add(Label("Discount") + ": " + A(d.DiscountMinor));
        Add(Label("Net") + ": " + A(d.NetMinor)); Add(Label("Tax") + ": " + A(d.TaxMinor));
        Add(Label("Total") + ": " + A(d.TotalMinor) + " " + d.Currency, true);
        Add(Label("Rounding") + ": " + A(d.RoundingMinor)); Add(Label("Due") + ": " + A(checked(d.TotalMinor + d.RoundingMinor)), true);
        foreach (var p in d.Payments)
        {
            Add(Label("Payment" + p.Method) + ": " + A(p.PaidMinor));
            if (p.Method == RetailCore.Domain.Sales.PaymentMethod.Cash) Add(Label("Tendered") + ": " + A(p.TenderedMinor));
        }
        Add(Label("Paid") + ": " + A(d.PaidMinor)); Add(Label("Change") + ": " + A(d.ChangeMinor), true);
        Add(d.Footer); return rows;
    }
}
