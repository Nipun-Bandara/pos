using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using RetailCore.Application.Foundation;
using RetailCore.Application.Reporting;
namespace RetailCore.Desktop;

public sealed partial class MainWindow
{
    private ScrollViewer TradingView()
    {
        var panel = new StackPanel();
        var tabs = new TabControl(); panel.Children.Add(tabs);
        tabs.Items.Add(new TabItem { Header = Texts.Get("BillHistory"), Content = BillHistoryView() });
        var cash = new StackPanel(); tabs.Items.Add(new TabItem { Header = Texts.Get("TradingCash"), Content = cash });
        Label(cash, "TradingCashHelp");
        var counted = Field(cash, "CashCountOrAmount"); var reason = Field(cash, "Reason");
        foreach (var (key, close, payOut) in new[] { ("CashPayIn", false, false), ("CashPayOut", false, true), ("CloseShift", true, false) })
            cash.Children.Add(Button(key, async () =>
            {
                if (!decimal.TryParse(counted.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var value) || value < 0) throw new FoundationException("InvalidCashCommand");
                var review = await _vm.Cash.ReviewAsync();
                long minor;
                try { minor = RetailCore.Domain.Monetary.Money.FromMajor(value, RetailCore.Domain.Monetary.Currency.FromCode(review.Currency)).MinorUnits; }
                catch (Exception ex) when (ex is OverflowException or RetailCore.Domain.Common.DomainException) { throw new FoundationException("InvalidCashCommand"); }
                var request = new CashRequest(Guid.NewGuid(), review.ShiftId, close, minor, payOut, reason.Text, review.ExpectedMinor);
                var owners = await _vm.Service.ApproversAsync();
                var dialog = new ApprovalDialog(_vm.Service, owners, CashService.Approval(request), $"{Texts.Get(key)}: {value}\n{Texts.Get("ExpectedCash")}: {review.ExpectedMinor / (decimal)RetailCore.Domain.Monetary.Currency.FromCode(review.Currency).MinorUnitsPerMajor}\n{Texts.Get("Reason")}: {reason.Text}") { Owner = this };
                if (dialog.ShowDialog() != true) return;
                var result = await _vm.Cash.PostAsync(request, dialog.Ticket);
                counted.Text = ""; reason.Text = "";
                MessageBox.Show(this, $"{Texts.Get("ExpectedCash")}: {result.ExpectedMinor / (decimal)RetailCore.Domain.Monetary.Currency.FromCode(review.Currency).MinorUnitsPerMajor}\n{Texts.Get("CashVariance")}: {result.VarianceMinor / (decimal)RetailCore.Domain.Monetary.Currency.FromCode(review.Currency).MinorUnitsPerMajor}", Texts.Get("TradingCash"));
            }));
        if (_vm.IsOwner)
        {
            var report = new StackPanel(); tabs.Items.Add(new TabItem { Header = Texts.Get("TradingReports"), Content = report });
            Label(report, "TradingReportHelp");
            Label(report, "BillFrom");
            var from = new DatePicker { SelectedDate = _vm.ReceiptSettings!.Today.ToDateTime(TimeOnly.MinValue) }; report.Children.Add(from);
            Label(report, "BillThrough");
            var through = new DatePicker { SelectedDate = from.SelectedDate }; report.Children.Add(through);
            AutomationProperties.SetAutomationId(from, "ReportFrom"); AutomationProperties.SetAutomationId(through, "ReportThrough");
            var kind = new ComboBox { ItemsSource = ReportService.Kinds.Select(k => new KeyValuePair<string, string>(k, Texts.Get(k))).ToArray(), DisplayMemberPath = "Value", SelectedIndex = 0 }; report.Children.Add(kind);
            AutomationProperties.SetAutomationId(kind, "ReportKind");
            var grid = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, Height = 400 };
            AutomationProperties.SetAutomationId(grid, "TradingReportTable");
            async Task<ReportTable> Load()
            {
                if (from.SelectedDate is not { } f || through.SelectedDate is not { } t || kind.SelectedItem is not KeyValuePair<string, string> k) throw new FoundationException("InvalidBillDates");
                var table = Localize(await _vm.Reports.LoadAsync(k.Key, new(DateOnly.FromDateTime(f), DateOnly.FromDateTime(t))));
                grid.Columns.Clear(); foreach (var (column, i) in table.Columns.Select((c, i) => (c, i))) grid.Columns.Add(new DataGridTextColumn { Header = column, Binding = new Binding($"[{i}]") { Mode = BindingMode.OneWay } }); grid.ItemsSource = table.Rows; return table;
            }
            report.Children.Add(Button("LoadReport", async () => { await Load(); }));
            foreach (var format in new[] { "CSV", "XLSX", "PDF" }) report.Children.Add(Button("Export" + format, async () => { await SaveExportAsync(await Load(), format); }));
            report.Children.Add(WithEmptyState(grid));
            ArrangeFields(report);
        }
        ArrangeFields(cash);
        return Scroll(panel);
    }
    private static ReportTable Localize(ReportTable table) => table with
    {
        Title = Texts.Get(table.Title),
        Columns = table.Columns.Select(Texts.Get).ToArray(),
        Rows = table.Rows.Select(row => row.Select((cell, i) => cell is string s && (table.Columns[i] is "AuditAction" or "MovementKind" or "BaseUnit" or "Unit" || s == "UnclassifiedCategory") ? Texts.Get(s) : cell).ToArray()).ToArray()
    };
    private Task SaveExportAsync(ReportTable table, string format)
    {
        var dialog = new SaveFileDialog { Filter = $"{format}|*.{format.ToLowerInvariant()}", DefaultExt = "." + format.ToLowerInvariant(), FileName = "RetailCore-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) };
        if (dialog.ShowDialog(this) != true) return Task.CompletedTask;
        byte[] bytes = format switch { "CSV" => TableExport.Csv(table), "XLSX" => TableExport.Xlsx(table), _ => ReportPdf.Render(table) };
        var temporary = dialog.FileName + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temporary, bytes); File.Move(temporary, dialog.FileName, true); _vm.ShowMessage("ExportSaved"); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return Task.CompletedTask;
    }
}
