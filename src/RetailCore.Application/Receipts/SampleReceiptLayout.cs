using System.Globalization;
using System.Resources;
using RetailCore.Domain.Monetary;
using RetailCore.Domain.Sales;

namespace RetailCore.Application.Receipts;

/// <summary>Fixed version 2, based on the owner's supplied receipt photograph. No template scripting.</summary>
public static class SampleReceiptLayout
{
    private static readonly ResourceManager Labels = new("RetailCore.Application.Receipts.SampleReceiptLabels", typeof(SampleReceiptLayout).Assembly);
    public static string Label(string key) => Labels.GetString(key, CultureInfo.InvariantCulture) ?? throw new InvalidOperationException(key);

    /// <summary>Product savings against snapshotted normal prices, excluding tender/cash rounding.</summary>
    public static long? SavingsMinor(ReceiptDocument d)
    {
        if (d.Lines.Any(l => l.NormalPriceMinor is null)) return null;
        try
        {
            var currency = Currency.FromCode(d.Currency);
            return d.Lines.Aggregate(0L, (total, l) => checked(total + Money.FromMinor(l.NormalPriceMinor!.Value, currency)
                .Extend(Quantity.FromThousandths(l.QuantityThousandths)).MinorUnits - l.TotalMinor));
        }
        catch (OverflowException) { return null; } // Unknown display value; never change or overflow financial records.
    }

    public static IReadOnlyList<ReceiptRow> Build(ReceiptDocument d, bool copy, SaleStatus status)
    {
        string A(long minor) => ReceiptLayout.Amount(minor, d.Currency);
        var rows = new List<ReceiptRow>();
        void Add(string? text, ReceiptStyle style = ReceiptStyle.Plain, bool bold = false)
        { if (!string.IsNullOrWhiteSpace(text)) rows.Add(new(text, bold, style)); }
        void Pair(string label, string value, bool bold = false) => rows.Add(new(label + " : " + value, bold, ReceiptStyle.Pair, label, value));
        Add(d.ShopName, ReceiptStyle.Title, true); Add(Label("Tagline"), ReceiptStyle.Center);
        Add(d.Address, ReceiptStyle.Center, true); Add(d.Telephone is null ? null : Label("Telephone") + " " + d.Telephone, ReceiptStyle.Center);
        if (!string.IsNullOrWhiteSpace(d.TaxRegistration)) Add(ReceiptLayout.Label("Registration") + ": " + d.TaxRegistration, ReceiptStyle.Center);
        rows.Add(new("", false, ReceiptStyle.Rule));
        if (copy) Add(ReceiptLayout.Label("Copy"), ReceiptStyle.Center, true);
        if (status == SaleStatus.Voided) Add(ReceiptLayout.Label("Voided"), ReceiptStyle.Center, true);
        Pair(ReceiptLayout.Label("Cashier"), d.Cashier); Pair(ReceiptLayout.Label("Bill"), d.Number);
        Pair(ReceiptLayout.Label("Date"), d.LocalTime);
        Pair(Label("Tender"), d.Payments.Select(p => p.Method).Distinct().Count() == 1
            ? ReceiptLayout.Label("Payment" + d.Payments[0].Method) : Label("Mixed"));
        if (d.LegacyHeader) Add(ReceiptLayout.Label("Legacy"), bold: true);
        var headers = new[] { Label("Quantity"), Label("Retail"), Label("OurPrice"), Label("LineTotal") };
        rows.Add(new(string.Join("  ", headers), true, ReceiptStyle.ColumnsHeader, headers[0], headers[1], headers[2], headers[3]));
        foreach (var l in d.Lines)
        {
            Add(l.Name + " (" + ReceiptLayout.Label("Unit" + l.Unit) + ")", ReceiptStyle.ItemName);
            var quantity = (l.QuantityThousandths / 1000m).ToString("0.000", CultureInfo.InvariantCulture);
            var retail = l.NormalPriceMinor is { } normal ? A(normal) : Label("Unknown");
            rows.Add(new(string.Join("  ", quantity, retail, A(l.UnitPriceMinor), A(l.TotalMinor)), false, ReceiptStyle.Columns,
                quantity, retail, A(l.UnitPriceMinor), A(l.TotalMinor)));
        }
        rows.Add(new("", false, ReceiptStyle.Rule));
        Pair(ReceiptLayout.Label("Total"), A(d.TotalMinor) + " " + d.Currency, true);
        if (d.DiscountMinor != 0) Pair(ReceiptLayout.Label("Discount"), A(d.DiscountMinor));
        if (d.TaxMinor != 0) Pair(ReceiptLayout.Label("Tax"), A(d.TaxMinor));
        if (d.RoundingMinor != 0)
        {
            Pair(ReceiptLayout.Label("Rounding"), A(d.RoundingMinor));
            Pair(ReceiptLayout.Label("Due"), A(checked(d.TotalMinor + d.RoundingMinor)), true);
        }
        Pair(Label("CashTender"), A(d.Payments.Where(p => p.Method == PaymentMethod.Cash).Sum(p => p.TenderedMinor)));
        Pair(Label("CardPaid"), A(d.Payments.Where(p => p.Method == PaymentMethod.Card).Sum(p => p.PaidMinor)));
        foreach (var p in d.Payments.Where(p => p.Method is not (PaymentMethod.Cash or PaymentMethod.Card)))
            Pair(ReceiptLayout.Label("Payment" + p.Method), A(p.PaidMinor));
        Pair(ReceiptLayout.Label("Change"), A(d.ChangeMinor), true);
        Add(Label("Savings") + " : " + (SavingsMinor(d) is { } savings ? A(savings) : Label("Unknown")), ReceiptStyle.Box, true);
        Pair(Label("Items"), d.Lines.Count.ToString(CultureInfo.InvariantCulture));
        Add(d.Footer, ReceiptStyle.Center); Add(Label("Thanks"), ReceiptStyle.Center, true);
        Add(Label("ValidationPending"), ReceiptStyle.Center); return rows;
    }
}
