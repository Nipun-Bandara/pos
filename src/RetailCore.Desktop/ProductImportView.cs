using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using RetailCore.Application.Catalogue;
using RetailCore.Application.Foundation;

namespace RetailCore.Desktop;

public sealed partial class MainWindow
{
    private Expander ProductImportView(CatalogueData data)
    {
        var panel = new StackPanel { IsEnabled = _vm.IsOwner };
        panel.Children.Add(new TextBlock { Text = Texts.Get("ImportHelp"), TextWrapping = TextWrapping.Wrap });
        foreach (var extension in new[] { ".csv", ".xlsx" })
            panel.Children.Add(Button(extension == ".csv" ? "ImportTemplateCSV" : "ImportTemplateXLSX", async () =>
            {
                var dialog = new SaveFileDialog { Filter = Texts.Get("ImportFileFilter"), DefaultExt = extension,
                    FilterIndex = extension == ".csv" ? 1 : 2, FileName = "RetailCore-products" + extension, AddExtension = true };
                if (dialog.ShowDialog(this) == true)
                    await File.WriteAllBytesAsync(dialog.FileName, ProductImportFile.Template(Path.GetExtension(dialog.FileName), Texts.Get("ImportProducts"), Texts.Get("ImportHelp")));
            }));
        Label(panel, "Category");
        var category = new ComboBox { ItemsSource = data.Categories.Where(c => c.Active).ToArray(),
            DisplayMemberPath = nameof(CategoryRow.Display), SelectedIndex = 0 };
        System.Windows.Automation.AutomationProperties.SetAutomationId(category, "ImportCategory");
        System.Windows.Automation.AutomationProperties.SetName(category, Texts.Get("Category"));
        panel.Children.Add(category);
        var path = Field(panel, "ImportFile");
        panel.Children.Add(Button("ChooseImportFile", () =>
        {
            var dialog = new OpenFileDialog { Filter = Texts.Get("ImportFileFilter"), CheckFileExists = true };
            if (dialog.ShowDialog(this) == true) path.Text = dialog.FileName;
            return Task.CompletedTask;
        }));
        var summaryFormat = CompositeFormat.Parse(Texts.Get("ImportSummaryFormat"));
        var summary = new TextBlock { TextWrapping = TextWrapping.Wrap };
        System.Windows.Automation.AutomationProperties.SetAutomationId(summary, "ImportSummary");
        var table = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, MaxHeight = 240 };
        System.Windows.Automation.AutomationProperties.SetAutomationId(table, "ImportPreviewTable");
        foreach (var (key, property) in new[] { ("ImportRow", "Row"), ("Sku", "Sku"), ("SinhalaName", "Name"), ("ImportError", "Error") })
            table.Columns.Add(new DataGridTextColumn { Header = Texts.Get(key), Binding = new Binding(property) { Mode = BindingMode.OneWay },
                ElementStyle = (Style)System.Windows.Application.Current.FindResource("PosWrappedCell") });
        byte[]? content = null; var extensionUsed = ""; var operation = Guid.Empty;
        Button? save = null;
        void ClearPreview()
        {
            content = null; operation = Guid.Empty; table.ItemsSource = null; summary.Text = "";
            if (save is not null) save.IsEnabled = false;
        }
        category.SelectionChanged += (_, _) => ClearPreview();
        path.TextChanged += (_, _) => ClearPreview();
        panel.Children.Add(Button("PreviewImport", async () =>
        {
            ClearPreview();
            if (category.SelectedItem is not CategoryRow selected) throw new FoundationException("InvalidCategory");
            var extension = Path.GetExtension(path.Text);
            if (!extension.Equals(".csv", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
                throw new FoundationException("InvalidImportFile");
            byte[] bytes;
            try
            {
                await using var file = new FileStream(path.Text, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
                if (file.Length == 0 || file.Length > ProductImportFile.MaximumBytes) throw new FoundationException("InvalidImportFile");
                bytes = new byte[checked((int)file.Length)]; await file.ReadExactlyAsync(bytes);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            { throw new FoundationException("ImportReadFailed"); }
            var preview = await _vm.Catalogue.PreviewImportAsync(bytes, extension, selected.Id);
            table.ItemsSource = preview.Rows.Select(r => new { r.Row, r.Sku, r.Name, Error = r.Error is null ? "" : Texts.Get(r.Error) }).ToArray();
            summary.Text = string.Format(CultureInfo.CurrentCulture, summaryFormat,
                preview.Rows.Count, preview.Rows.Count(r => r.Error is not null));
            if (preview.CanImport) { content = bytes; extensionUsed = extension; operation = Guid.NewGuid(); save!.IsEnabled = true; }
        }));
        panel.Children.Add(summary); panel.Children.Add(table);
        save = Button("ImportProducts", async () =>
        {
            if (content is null || category.SelectedItem is not CategoryRow selected) throw new FoundationException("ImportInvalidRows");
            await _vm.Catalogue.ImportAsync(operation, content, extensionUsed, selected.Id);
            await _vm.NavigateAsync("Products"); _vm.ShowMessage("ProductsImported");
        });
        save.IsEnabled = false; panel.Children.Add(save);
        return new Expander { Header = Texts.Get("ImportProducts"), Content = panel, Margin = new Thickness(0, 12, 0, 12) };
    }
}
