using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using RetailCore.Application.Catalogue;
using RetailCore.Application.Foundation;
using RetailCore.Domain.Catalogue;
using RetailCore.Domain.Common;

namespace RetailCore.Desktop;

public sealed partial class MainWindow
{
    private static readonly string[] ProductExportColumns=["Sku","SinhalaName","EnglishName","ReceiptName","Barcode","CurrentCategory","Cost","NormalPrice","PriceBands","StockBalance","Unit","BaseUnit"];
    private ScrollViewer ProductsView()
    {
        var data = _vm.CatalogueData!;
        var panel = new StackPanel();
        var search = Field(panel, "ProductSearch");
        var view = new ListCollectionView(data.Products.ToList());
        search.TextChanged += (_, _) =>
        {
            var term = search.Text.Trim();
            view.Filter = item => item is ProductRow row &&
                new[] { row.Sku, row.SinhalaName, row.EnglishName, row.Barcode, row.Category }
                .Any(value => value.Contains(term, StringComparison.CurrentCultureIgnoreCase));
        };
        var table = new DataGrid { ItemsSource = view, AutoGenerateColumns = false, IsReadOnly = true,
            CanUserAddRows = false, CanUserSortColumns = true, Height = 280, SelectionMode = DataGridSelectionMode.Single };
        foreach (var (key, property) in new[] { ("Sku", "Sku"), ("SinhalaName", "SinhalaName"),
            ("EnglishName", "EnglishName"), ("Barcode", "Barcode"), ("Category", "Category"),
            ("Cost", "Cost"), ("NormalPrice", "NormalPrice"), ("PriceBands", "Bands"), ("StockBalance", "Stock"), ("BaseUnit", "StockUnit") })
            table.Columns.Add(new DataGridTextColumn { Header = Texts.Get(key), Binding = new Binding(property) { Mode = BindingMode.OneWay, Converter = property == "StockUnit" ? new EnumTextConverter("Unit") : null }, SortMemberPath = property });
        // CheckBox.IsChecked defaults to TwoWay even when its DataGrid is read-only.
        // ProductRow is a query projection; edits belong to the separate product form.
        table.Columns.Add(new DataGridCheckBoxColumn { Header = Texts.Get("Active"), Binding = new Binding(nameof(ProductRow.Active)) { Mode = BindingMode.OneWay }, IsReadOnly = true, SortMemberPath = nameof(ProductRow.Active) });
        System.Windows.Automation.AutomationProperties.SetAutomationId(table, "ProductsTable");
        panel.Children.Add(table);
        foreach(var (key,filtered,format,prices) in new[]{("ExportAllCSV",false,"CSV",false),("ExportFilteredCSV",true,"CSV",false),("ExportAllXLSX",false,"XLSX",false),("ExportFilteredXLSX",true,"XLSX",false),("PriceListPDF",true,"PDF",true)})
            panel.Children.Add(Button(key,async()=>
            {
                var current=await _vm.Catalogue.LoadAsync(); // Recheck authentication and export the current saved catalogue.
                var ids=filtered?view.Cast<ProductRow>().Select(p=>p.Product.Id).ToHashSet():current.Products.Select(p=>p.Product.Id).ToHashSet();
                var rows=current.Products.Where(p=>ids.Contains(p.Product.Id)).Select(p=>prices
                    ?new object?[]{p.Sku,p.Product.ReceiptName,p.Barcode,p.NormalPrice,p.Bands,Texts.Get("Unit"+p.Product.Unit)}
                    :new object?[]{p.Sku,p.SinhalaName,p.EnglishName,p.Product.ReceiptName,p.Barcode,p.Category,p.Cost,p.NormalPrice,p.Bands,p.Stock,Texts.Get("Unit"+p.Product.Unit),Texts.Get("Unit"+p.StockUnit)}).ToArray();
                var local=await _vm.Receipts.SettingsAsync();
                var context=$"{Texts.Get("LocalDate")}: {local.Today:yyyy-MM-dd} · {current.Currency} · {Texts.Get(filtered?"FilteredProducts":"AllProducts")}: {(filtered?search.Text: string.Empty)}";
                await SaveExportAsync(new RetailCore.Application.Reporting.ReportTable(Texts.Get(prices?"PriceListPDF":"Products"),context,
                    prices?new[]{Texts.Get("Sku"),Texts.Get("ReceiptName"),Texts.Get("Barcode"),Texts.Get("NormalPrice"),Texts.Get("PriceBands"),Texts.Get("Unit")}:ProductExportColumns.Select(Texts.Get).ToArray(),rows),format);
            }));

        panel.Children.Add(new TextBlock { Text = Texts.Get("CatalogueHelp"), TextWrapping = TextWrapping.Wrap });
        // Owners edit; every application command independently rechecks authority.
        var editor = new StackPanel { IsEnabled = _vm.IsOwner };
        panel.Children.Add(editor);
        ProductId? selectedId = null;
        var sku = Field(editor, "Sku");
        var sinhala = Field(editor, "SinhalaName");
        var english = Field(editor, "EnglishName");
        var receipt = Field(editor, "ReceiptName");
        var barcode = Field(editor, "Barcode");
        Label(editor, "Category");
        var category = new ComboBox { ItemsSource = data.Categories, DisplayMemberPath = nameof(CategoryRow.Display), SelectedIndex = 0 };
        editor.Children.Add(category);
        Label(editor, "Unit");
        var units = Enum.GetValues<UnitOfMeasure>();
        var unit = new ComboBox { ItemsSource = units.Select(u => Texts.Get("Unit" + u)).ToArray(), SelectedIndex = 0 };
        editor.Children.Add(unit);
        var cost = Field(editor, "Cost", "0");
        var normal = Field(editor, "NormalPrice", "100");
        var bandFields = new List<(TextBox Minimum, TextBox Price)>();
        for (var i = 0; i < 3; i++)
        {
            editor.Children.Add(new TextBlock { Text = Texts.Get("QuantityBreak") + " " + (i + 1) });
            bandFields.Add((Field(editor, "Minimum" + (i + 1)), Field(editor, "BandPrice" + (i + 1))));
        }
        var active = new CheckBox { Content = Texts.Get("Active"), IsChecked = true };
        editor.Children.Add(active);
        static decimal Number(TextBox box) => decimal.TryParse(box.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var value)
            ? value : throw new FoundationException("InvalidNumber");
        ProductInput Input()
        {
            if (unit.SelectedIndex < 0) throw new FoundationException("InvalidProduct");
            if (category.SelectedItem is not CategoryRow c) throw new FoundationException("InvalidCategory");
            var bands = new List<BandInput>();
            var gap = false;
            foreach (var pair in bandFields)
            {
                if (string.IsNullOrWhiteSpace(pair.Minimum.Text) && string.IsNullOrWhiteSpace(pair.Price.Text)) { gap = true; continue; }
                if (gap) throw new FoundationException("InvalidPricing");
                bands.Add(new BandInput(Number(pair.Minimum), Number(pair.Price)));
            }
            return new ProductInput(selectedId, sku.Text, sinhala.Text, english.Text, receipt.Text, c.Id,
                units[unit.SelectedIndex], Number(cost), Number(normal), barcode.Text, bands, active.IsChecked == true);
        }
        editor.Children.Add(Button("SaveProduct", async () =>
        {
            await _vm.Catalogue.SaveProductAsync(Input());
            await _vm.NavigateAsync("Products");
            _vm.ShowSaved();
        }));
        editor.Children.Add(Button("NewProduct", () =>
        {
            selectedId = null;
            table.UnselectAll();
            foreach (var field in new[] { sku, sinhala, english, receipt, barcode }) field.Clear();
            cost.Text = "0"; normal.Text = "100"; unit.SelectedIndex = 0; active.IsChecked = true;
            foreach (var pair in bandFields) { pair.Minimum.Clear(); pair.Price.Clear(); }
            return Task.CompletedTask;
        }));
        // Preview remains available to cashiers using the selected persisted product.
        var quantity = Field(panel, "PreviewQuantity", "1");
        var preview = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) };
        System.Windows.Automation.AutomationProperties.SetAutomationId(preview, "PricePreview");
        panel.Children.Add(preview);
        void RefreshPreview()
        {
            try
            {
                var input = _vm.IsOwner ? Input() : (table.SelectedItem as ProductRow)?.Product;
                if (input is null) { preview.Text = Texts.Get("SelectProduct"); return; }
                var quote = CatalogueService.Preview(input, data.Currency, Number(quantity));
                preview.Text = $"{Texts.Get("AppliedUnitPrice")}: {quote.UnitPrice.ToDecimal():0.00} {data.Currency} · " +
                    $"{Texts.Get("AppliedBand")}: {quote.Band + 1} · {Texts.Get("PreviewTotal")}: {quote.Total.ToDecimal():0.00} · " +
                    $"{Texts.Get("Savings")}: {quote.Savings.ToDecimal():0.00}";
            }
            catch (FoundationException ex) { preview.Text = Texts.Get(ex.Key); }
        }
        foreach (var field in new[] { sku, sinhala, english, receipt, barcode, cost, normal, quantity }
            .Concat(bandFields.SelectMany(p => new[] { p.Minimum, p.Price }))) field.TextChanged += (_, _) => RefreshPreview();
        category.SelectionChanged += (_, _) => RefreshPreview();
        unit.SelectionChanged += (_, _) => RefreshPreview();
        table.SelectionChanged += (_, _) =>
        {
            if (table.SelectedItem is not ProductRow row) return;
            var p = row.Product;
            selectedId = p.Id;
            sku.Text = p.Sku; sinhala.Text = p.SinhalaName; english.Text = p.EnglishName; receipt.Text = p.ReceiptName;
            barcode.Text = p.Barcode; cost.Text = p.Cost.ToString(CultureInfo.CurrentCulture);
            normal.Text = p.NormalPrice.ToString(CultureInfo.CurrentCulture);
            category.SelectedItem = data.Categories.SingleOrDefault(c => c.Id == p.CategoryId);
            unit.SelectedIndex = Array.IndexOf(units, p.Unit); active.IsChecked = p.Active;
            for (var i = 0; i < 3; i++)
            {
                bandFields[i].Minimum.Text = i < p.Bands.Count ? p.Bands[i].Minimum.ToString(CultureInfo.CurrentCulture) : "";
                bandFields[i].Price.Text = i < p.Bands.Count ? p.Bands[i].Price.ToString(CultureInfo.CurrentCulture) : "";
            }
            RefreshPreview();
        };
        var categories = new StackPanel { IsEnabled = _vm.IsOwner };
        panel.Children.Add(categories);
        Label(categories, "CategoryManagement");
        var categoryChoice = new ComboBox { ItemsSource = data.Categories, DisplayMemberPath = nameof(CategoryRow.Display) };
        categories.Children.Add(categoryChoice);
        var categorySi = Field(categories, "CategorySinhala");
        var categoryEn = Field(categories, "CategoryEnglish");
        var categoryActive = new CheckBox { Content = Texts.Get("Active"), IsChecked = true };
        categories.Children.Add(categoryActive);
        categoryChoice.SelectionChanged += (_, _) =>
        {
            if (categoryChoice.SelectedItem is not CategoryRow c) return;
            categorySi.Text = c.SinhalaName; categoryEn.Text = c.EnglishName; categoryActive.IsChecked = c.Active;
        };
        categories.Children.Add(Button("NewCategory", () =>
        {
            categoryChoice.SelectedItem = null; categorySi.Clear(); categoryEn.Clear(); categoryActive.IsChecked = true;
            return Task.CompletedTask;
        }));
        categories.Children.Add(Button("SaveCategory", async () =>
        {
            await _vm.Catalogue.SaveCategoryAsync((categoryChoice.SelectedItem as CategoryRow)?.Id,
                categorySi.Text, categoryEn.Text, categoryActive.IsChecked == true);
            await _vm.NavigateAsync("Products");
            _vm.ShowSaved();
        }));
        RefreshPreview();
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
}
