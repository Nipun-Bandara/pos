using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using RetailCore.Application.Foundation;
using RetailCore.Application.Inventory;
using RetailCore.Domain.Inventory;
using RetailCore.Domain.Security;

namespace RetailCore.Desktop;

internal sealed class EnumTextConverter(string prefix) : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is null ? string.Empty : prefix == "Reason" ? value is "Sale" ? Texts.Get("StockKindSale") : value : Texts.Get(prefix + value);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed partial class MainWindow
{
    private ScrollViewer StockView()
    {
        var data = _vm.StockData!;
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = Texts.Get("StockHelp"), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(Button("RefreshStock", () => _vm.NavigateAsync("Stock")));
        var search = Field(panel, "StockSearch");
        var low = new CheckBox { Content = Texts.Get("LowStockOnly"), Margin = new Thickness(0, 10, 0, 10) };
        panel.Children.Add(low);
        var view = new ListCollectionView(data.Products.ToList());
        void Filter()
        {
            view.Filter = item => item is StockRow row && (low.IsChecked != true || row.LowStock)
                && row.Display.Contains(search.Text.Trim(), StringComparison.CurrentCultureIgnoreCase);
        }
        search.TextChanged += (_, _) => Filter();
        low.Checked += (_, _) => Filter(); low.Unchecked += (_, _) => Filter();
        var table = StockTable(view, "StockTable", ("Sku", "Sku", null), ("SinhalaName", "SinhalaName", null),
            ("EnglishName", "EnglishName", null), ("BaseUnit", "BaseUnit", "Unit"),
            ("StockBalance", "Balance", null), ("StockFactor", "Factor", null), ("Reorder", "Reorder", null));
        panel.Children.Add(WithEmptyState(table));
        Label(panel, "StockProduct");
        var product = new ComboBox { ItemsSource = data.Products, DisplayMemberPath = nameof(StockRow.Display), SelectedIndex = 0 };
        AutomationProperties.SetAutomationId(product, "StockProduct");
        panel.Children.Add(product);
        var balance = new TextBlock { Margin = new Thickness(0, 12, 0, 12), TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetAutomationId(balance, "SelectedStockBalance");
        panel.Children.Add(balance);
        Label(panel, "StockHistory");
        var history = StockTable(null, "StockHistory", ("StockDate", "OccurredAt", null), ("StockKind", "Kind", "StockKind"),
            ("EnteredQuantity", "InputQuantity", null), ("StockFactor", "Factor", null), ("BaseQuantity", "SignedBaseQuantity", null),
            ("BaseUnit", "BaseUnit", "Unit"), ("Cost", "InputUnitCost", null), ("BaseCost", "UnitCost", null),
            ("StockReason", "Reason", "Reason"), ("StockReference", "Reference", null), ("RecordedBy", "RecordedByName", null),
            ("ApprovedBy", "ApprovedByName", null), ("StockSource", "SourceType", "StockSource"), ("StockSourceId", "SourceId", null));
        panel.Children.Add(WithEmptyState(history));
        var ownerPanel = new StackPanel { IsEnabled = _vm.IsOwner };
        panel.Children.Add(ownerPanel);
        Label(ownerPanel, "StockConfiguration");
        var baseProduct = new ComboBox { ItemsSource = data.Products.Where(p => p.Id == p.BaseProductId).ToArray(), DisplayMemberPath = nameof(StockRow.Display) };
        AutomationProperties.SetAutomationId(baseProduct, "StockBaseProduct");
        ownerPanel.Children.Add(baseProduct);
        var factor = Field(ownerPanel, "StockFactor", "1");
        var reorder = Field(ownerPanel, "Reorder", "0");
        static decimal Number(TextBox box) => decimal.TryParse(box.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var number)
            ? number : throw new FoundationException("InvalidNumber");
        StockRow Selected() => product.SelectedItem as StockRow ?? throw new FoundationException("SelectProduct");
        ownerPanel.Children.Add(Button("SaveStockConfiguration", async () =>
        {
            if (baseProduct.SelectedItem is not StockRow root) throw new FoundationException("InvalidStockConfiguration");
            await _vm.Stock.ConfigureAsync(new StockConfiguration(Selected().Id, root.Id, Number(factor), Number(reorder)));
            await _vm.NavigateAsync("Stock"); _vm.ShowSaved();
        }));
        Label(ownerPanel, "StockEntry");
        var kinds = Enum.GetValues<StockEntryKind>();
        var kind = new ComboBox { ItemsSource = kinds.Select(k => Texts.Get("Entry" + k)).ToArray(), SelectedIndex = 0 };
        AutomationProperties.SetAutomationId(kind, "StockEntryKind");
        ownerPanel.Children.Add(kind);
        var quantity = Field(ownerPanel, "EnteredQuantity", "1");
        var cost = Field(ownerPanel, "StockInputCost", "0");
        Label(ownerPanel, "StockDate");
        var zone = TimeZoneInfo.FindSystemTimeZoneById(_vm.Profile?.TimeZoneId ?? "Asia/Colombo");
        var date = new DatePicker { SelectedDate = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).Date };
        AutomationProperties.SetAutomationId(date, "StockDate");
        ownerPanel.Children.Add(date);
        var reason = Field(ownerPanel, "StockReason");
        var reference = Field(ownerPanel, "StockReference");
        var allowNegative = new CheckBox { Content = Texts.Get("AllowNegativeStock") };
        ownerPanel.Children.Add(allowNegative);
        var entryPreview = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) };
        AutomationProperties.SetAutomationId(entryPreview, "StockEntryPreview");
        ownerPanel.Children.Add(entryPreview);
        var operationId = Guid.NewGuid();
        StockEntryInput Input()
        {
            var row = Selected();
            if (kind.SelectedIndex < 0 || date.SelectedDate is not { } localDate) throw new FoundationException("InvalidStockEntry");
            var inputZone = TimeZoneInfo.FindSystemTimeZoneById(_vm.Profile!.TimeZoneId);
            var occurred = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localDate, DateTimeKind.Unspecified), inputZone));
            return new StockEntryInput(operationId, row.Id, kinds[kind.SelectedIndex], Number(quantity), Number(cost), reason.Text,
                occurred, reference.Text, allowNegative.IsChecked == true, row.Revision, checked((long)(row.Balance * 1000m)));
        }
        void EntryPreview()
        {
            try
            {
                var row = Selected();
                var number = Number(quantity);
                var entered = RetailCore.Domain.Monetary.Quantity.Of(number);
                if (!RetailCore.Domain.Catalogue.UnitOfMeasureExtensions.AllowsFractionalQuantity(row.SellingUnit) && !entered.IsWholeUnits)
                    throw new FoundationException("InvalidStockQuantity");
                var converted = kinds[kind.SelectedIndex] == StockEntryKind.Count ? 0m
                    : StockConversion.ToBase(entered, checked((long)(row.Factor * 1000m))).Value;
                var delta = kinds[kind.SelectedIndex] switch
                {
                    StockEntryKind.Count => number - row.Balance,
                    StockEntryKind.Wastage or StockEntryKind.AdjustmentOut => -converted,
                    _ => converted,
                };
                entryPreview.Text = $"{Texts.Get("StockVariance")}: {delta:0.###} · {Texts.Get("StockAfter")}: {row.Balance + delta:0.###} {Texts.Get("Unit" + row.BaseUnit)}"
                    + (row.Balance + delta < 0 ? "\n" + Texts.Get("InsufficientStock") : "");
            }
            catch (FoundationException ex) { entryPreview.Text = Texts.Get(ex.Key); }
            catch (RetailCore.Domain.Common.DomainException ex) { entryPreview.Text = Texts.Get(ex.Message == "StockPrecision" ? "StockPrecision" : "InvalidStockQuantity"); }
            catch (OverflowException) { entryPreview.Text = Texts.Get("InvalidStockQuantity"); }
        }
        quantity.TextChanged += (_, _) => EntryPreview(); kind.SelectionChanged += (_, _) => EntryPreview();
        ownerPanel.Children.Add(Button("PostStockEntry", async () =>
        {
            var input = Input();
            ApprovalTicket? ticket = null;
            if (input.Kind is StockEntryKind.AdjustmentIn or StockEntryKind.AdjustmentOut or StockEntryKind.Wastage or StockEntryKind.Count || input.AllowNegative)
            {
                var review = $"{Selected().Display}\n{Texts.Get("StockKind")}: {Texts.Get("Entry" + input.Kind)}\n"
                    + $"{Texts.Get("EnteredQuantity")}: {input.Quantity}\n{Texts.Get("StockFactor")}: {Selected().Factor}\n"
                    + $"{Texts.Get("StockInputCost")}: {input.UnitCost} {data.Currency}\n{Texts.Get("StockDate")}: {date.SelectedDate:d}\n"
                    + $"{Texts.Get("StockReason")}: {input.Reason}\n{Texts.Get("StockReference")}: {input.Reference}\n{entryPreview.Text}";
                var dialog = new ApprovalDialog(_vm.Service, _vm.Users.Where(u => u.IsActive && u.Role == Role.Owner).ToArray(), StockService.EntryApproval(input), review) { Owner = this };
                if (dialog.ShowDialog() != true) return;
                ticket = dialog.Ticket;
            }
            await _vm.Stock.PostAsync(input, ticket);
            await _vm.NavigateAsync("Stock"); _vm.ShowSaved();
        }));
        Label(panel, "ConsumptionHelp");
        var looseQuantity = Field(panel, "ConsumptionQuantity", "1.250");
        Label(panel, "ConsumptionPack");
        var packProduct = new ComboBox { ItemsSource = data.Products, DisplayMemberPath = nameof(StockRow.Display), SelectedIndex = 0 };
        AutomationProperties.SetAutomationId(packProduct, "ConsumptionPack");
        panel.Children.Add(packProduct);
        var packQuantity = Field(panel, "ConsumptionPackQuantity", "1");
        var consumptionPreview = new TextBlock { TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetAutomationId(consumptionPreview, "ConsumptionResult");
        panel.Children.Add(consumptionPreview);
        panel.Children.Add(Button("PreviewConsumption", async () =>
        {
            if (packProduct.SelectedItem is not StockRow pack) throw new FoundationException("SelectProduct");
            var previews = await _vm.Stock.PreviewConsumptionAsync([new(Selected().Id, Number(looseQuantity)), new(pack.Id, Number(packQuantity))]);
            consumptionPreview.Text = string.Join('\n', previews.Select(p =>
                $"{data.Products.Single(r => r.Id == p.BaseProductId).Display}: {Texts.Get("StockBefore")}: {p.Before:0.###} · "
                + $"{Texts.Get("StockConsumed")}: {p.Consumed:0.###} · {Texts.Get("StockAfter")}: {p.After:0.###}"
                + (p.Insufficient ? " — " + Texts.Get("InsufficientStock") : "")));
        }));
        void Select()
        {
            if (product.SelectedItem is not StockRow row) return;
            balance.Text = $"{Texts.Get("StockBalance")}: {row.Balance:0.###} {Texts.Get("Unit" + row.BaseUnit)}";
            history.ItemsSource = data.History.Where(m => m.BaseProductId == row.BaseProductId).Reverse().ToArray();
            baseProduct.SelectedItem = baseProduct.Items.Cast<StockRow>().SingleOrDefault(p => p.Id == row.BaseProductId);
            factor.Text = row.Factor.ToString(CultureInfo.CurrentCulture);
            reorder.Text = (row.Id == row.BaseProductId ? row.Reorder : 0).ToString(CultureInfo.CurrentCulture);
            EntryPreview();
        }
        product.SelectionChanged += (_, _) => Select();
        table.SelectionChanged += (_, _) => { if (table.SelectedItem is StockRow row) product.SelectedItem = row; };
        Select();
        ArrangeFields(panel);
        return Scroll(panel);
    }

    private static DataGrid StockTable(object? source, string id, params (string Key, string Property, string? Prefix)[] columns)
    {
        var table = new DataGrid { ItemsSource = source as System.Collections.IEnumerable, AutoGenerateColumns = false,
            IsReadOnly = true, CanUserAddRows = false, CanUserSortColumns = true, Height = 230, SelectionMode = DataGridSelectionMode.Single };
        AutomationProperties.SetAutomationId(table, id);
        foreach (var column in columns)
            table.Columns.Add(new DataGridTextColumn { Header = Texts.Get(column.Key), SortMemberPath = column.Property,
                ElementStyle = (Style)System.Windows.Application.Current.FindResource("PosWrappedCell"),
                Binding = new Binding(column.Property) { Mode = BindingMode.OneWay, Converter = column.Prefix is null ? null : new EnumTextConverter(column.Prefix) } });
        return table;
    }
}
