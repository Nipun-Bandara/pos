using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using RetailCore.Application.Foundation;
using RetailCore.Application.Receipts;

namespace RetailCore.Desktop;

public sealed partial class MainWindow
{
    private DateOnly? _billFrom;
    private DateOnly? _billThrough;
    private ScrollViewer BillHistoryView()
    {
        var settings = _vm.ReceiptSettings!;
        var panel = new StackPanel(); Label(panel, "BillHistory");
        panel.Children.Add(new TextBlock { Text = Texts.Get("BillHistoryHelp"), TextWrapping = TextWrapping.Wrap });
        Label(panel, "BillFrom"); var from = new DatePicker { SelectedDate = (_billFrom ?? settings.Today).ToDateTime(TimeOnly.MinValue) };
        AutomationProperties.SetAutomationId(from, "BillFrom"); panel.Children.Add(from);
        Label(panel, "BillThrough"); var through = new DatePicker { SelectedDate = (_billThrough ?? settings.Today).ToDateTime(TimeOnly.MinValue) };
        AutomationProperties.SetAutomationId(through, "BillThrough"); panel.Children.Add(through);
        panel.Children.Add(Button("FilterBills", async () =>
        {
            if (from.SelectedDate is not { } f || through.SelectedDate is not { } t) throw new FoundationException("InvalidBillDates");
            _billFrom = DateOnly.FromDateTime(f); _billThrough = DateOnly.FromDateTime(t);
            await _vm.LoadBillsAsync(new(_billFrom.Value, _billThrough.Value));
        }));
        var lookup = Field(panel, "BillNumberLookup");
        panel.Children.Add(Button("FindBill", () => _vm.OpenBillAsync(number: lookup.Text)));
        lookup.KeyDown += async (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.Enter) return;
            e.Handled = true; await _vm.RunAsync(() => _vm.OpenBillAsync(number: lookup.Text));
        };
        var table = StockTable(_vm.Bills, "BillHistoryTable", ("BillNumber", "Number", null), ("BillLocalDate", "LocalTime", null),
            ("BillTotal", "Total", null), ("Currency", "Currency", null), ("BillStatus", "Status", "BillStatus"));
        table.Height = 190; panel.Children.Add(table);
        panel.Children.Add(Button("OpenBill", async () =>
        {
            if (table.SelectedItem is not BillSummary bill) throw new FoundationException("SelectBill");
            await _vm.OpenBillAsync(bill.SaleId);
        }));
        if (_vm.SelectedBill is { } detail)
        {
            var d = detail.Receipt;
            panel.Children.Add(new TextBlock { Text = d.Number + " · " + Texts.Get("BillStatus" + detail.Status), FontSize = 24 });
            panel.Children.Add(new TextBlock { Text = Texts.Get(d.Version == 2 ? "SampleReceipt" : "ProvisionalReceipt"), TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(new TextBlock { Text = $"{Texts.Get("ReceiptPrinter")}: {settings.Printer ?? Texts.Get("PrinterNotConfigured")} · {Texts.Get("PrintableWidth")}: {settings.PrintableWidthMillimeters}", TextWrapping = TextWrapping.Wrap });
            if (d.LegacyHeader) panel.Children.Add(new TextBlock { Text = Texts.Get("LegacyBillHeader"), TextWrapping = TextWrapping.Wrap });
            // Receipt content always resolves Sinhala labels, independent of interface culture.
            var text = new TextBox { Text = string.Join('\n', ReceiptLayout.Build(d, detail.Prints.Count > 0, detail.Status).Select(r => r.Text)),
                IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 270, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            AutomationProperties.SetAutomationId(text, "SavedReceiptText"); panel.Children.Add(text);
            panel.Children.Add(StockTable(d.Lines.Select(l => new { l.Sku, l.Name, Quantity = l.QuantityThousandths / 1000m,
                Price = ReceiptLayout.Amount(l.UnitPriceMinor, d.Currency), Cost = ReceiptLayout.Amount(l.CostMinor, d.Currency), l.Unit,
                Normal = l.NormalPriceMinor is { } n ? ReceiptLayout.Amount(n, d.Currency) : "", Tier = l.TierPriceMinor is { } t ? ReceiptLayout.Amount(t, d.Currency) : "",
                Band = l.TierBand is { } b ? (b + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) : "", l.TaxCode, l.TaxCategory, l.TaxRate, Tax = ReceiptLayout.Amount(l.TaxMinor, d.Currency), l.ManualReason }), "SavedBillLines",
                ("Sku", "Sku", null), ("ReceiptName", "Name", null), ("BillingQuantity", "Quantity", null), ("ChargedPrice", "Price", null),
                ("Unit", "Unit", "Unit"), ("Cost", "Cost", null), ("NormalPrice", "Normal", null), ("TierPrice", "Tier", null), ("QualifiedBand", "Band", null),
                ("BillTaxCode", "TaxCode", null), ("BillTaxCategory", "TaxCategory", "BillTax"), ("BillTaxRate", "TaxRate", null),
                ("BillTax", "Tax", null), ("DiscountReason", "ManualReason", null)));
            panel.Children.Add(Button("ReturnThisBill", async () => { await _vm.NavigateAsync("Returns"); await _vm.FindReturnBillAsync(d.Number); }));
            if(detail.Returns is { Count: >0 } linked)
                panel.Children.Add(StockTable(linked.Select(r=>new {r.Number,Credit=ReceiptLayout.Amount(r.CreditMinor,d.Currency),r.ReplacementNumber}),"BillReturns",("BillNumber","Number",null),("ReturnCredit","Credit",null),("ReplacementBill","ReplacementNumber",null)));
            panel.Children.Add(Button("PreviewReceipt", () =>
            {
                var document = ReceiptRenderer.Render(d, settings.PrintableWidthMillimeters, detail.Prints.Count > 0, detail.Status);
                var paginator = ((IDocumentPaginatorSource)document).DocumentPaginator; paginator.ComputePageCount();
                var pages = new StackPanel();
                for (var i = 0; i < paginator.PageCount; i++)
                {
                    pages.Children.Add(new TextBlock { Text = ReceiptLayout.Label("Page") + " " + (i + 1) });
                    pages.Children.Add(new DocumentPageView { DocumentPaginator = paginator, PageNumber = i, Margin = new Thickness(8) });
                }
                var preview = new Window { Owner = this, Title = Texts.Get(d.Version == 2 ? "SampleReceipt" : "ProvisionalReceipt"), Width = 550, Height = 720,
                    Content = new ScrollViewer { Content = pages, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto } };
                preview.ShowDialog(); return Task.CompletedTask;
            }));
            panel.Children.Add(Button(detail.Prints.Count == 0 ? "PrintReceipt" : "ReprintReceipt", () => _vm.PrintBillAsync(d.SaleId, new WindowsReceiptPrinter(Dispatcher))));
            panel.Children.Add(new TextBlock { Text = Texts.Get("PrintRecoveryHelp"), TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(StockTable(detail.Prints.Select(p => new { At = p.Attempt.RequestedAt.ToString("u"), p.Attempt.Printer,
                p.Attempt.PrintableWidthMillimeters, Copy = Texts.Get(p.Attempt.IsCopy ? "ReprintReceipt" : "PrintReceipt"),
                Status = Texts.Get(p.Outcome?.Status ?? "PrintUnknown") }), "ReceiptPrintHistory", ("PrintDateUtc", "At", null),
                ("ReceiptPrinter", "Printer", null), ("PrintableWidth", "PrintableWidthMillimeters", null), ("PrintCopy", "Copy", null), ("PrintStatus", "Status", null)));
        }
        panel.Loaded += (_, _) => lookup.Focus();
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
}
