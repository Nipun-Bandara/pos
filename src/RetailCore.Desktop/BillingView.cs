using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using RetailCore.Application.Checkout;
using RetailCore.Application.Foundation;
using RetailCore.Domain.Common;
using RetailCore.Domain.Monetary;
using RetailCore.Domain.Sales;

namespace RetailCore.Desktop;

public sealed partial class MainWindow
{
    private Panel? _billingActions;
    private TextBox? _billingBarcode;
    private TextBox? _billingTender;
    private TextBox? _billingSearchBox;
    private Button? _billingPay;
    private Button? _billingRemove;
    private Task _billingScanQueue = Task.CompletedTask;
    private Guid _billingUiSession;
    private string _billingSearch = "";
    private string _billingTenderText = "0";
    private CategoryId? _billingCategory;
    private ProductId? _billingSelectedLine;

    private void HandleBillingShortcut(object sender, KeyEventArgs e)
    {
        if (_vm.Screen != "Shell" || _vm.Section != "Billing") return;
        if (e.Key == Key.F2 || e.Key == Key.Escape) { _billingBarcode?.Focus(); e.Handled = true; }
        if (_vm.Busy) return;
        if (e.Key == Key.F4) { _billingTender?.Focus(); _billingTender?.SelectAll(); _billingTender?.BringIntoView(); e.Handled = true; }
        if (e.Key == Key.F6 && _billingRemove?.IsEnabled == true) { _billingRemove.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); e.Handled = true; }
        if (e.Key == Key.F9 && _billingPay?.IsEnabled == true) { _billingPay.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); e.Handled = true; }
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { _billingSearchBox?.Focus(); e.Handled = true; }
    }

    private async Task QueueScanAsync(Task previous, string barcode, decimal quantity, Guid sessionVersion)
    {
        await previous;
        while (_vm.Busy) await Task.Delay(15);
        if (_vm.Session.Version != sessionVersion || _vm.Session.IsLocked || _vm.BillingState is null) return;
        await _vm.RunAsync(async () =>
        {
            var cart = _vm.BillingState.Basket;
            _vm.SetBillingState(await _vm.Billing.ScanAsync(cart.Id, cart.Version, barcode, quantity));
        });
    }
    private void RequireScansFinished()
    {
        if (!_billingScanQueue.IsCompleted) throw new FoundationException("ScanInProgress");
    }

    private BillingWorkspace BillingView()
    {
        var state = _vm.BillingState!;
        var q = state.Quote;
        var currency = Currency.FromCode(q.Currency);
        string Amount(long minor) => Money.FromMinor(minor, currency).ToDecimal().ToString("0.00", CultureInfo.CurrentCulture);
        var workspace = new BillingWorkspace();
        var panel = new StackPanel(); workspace.ScanHost.Content = panel;
        var scanRow = new Grid();
        scanRow.ColumnDefinitions.Add(new ColumnDefinition());
        scanRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
        scanRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var barcodeField = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        var weightField = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        scanRow.Children.Add(barcodeField); Grid.SetColumn(weightField, 1); scanRow.Children.Add(weightField);
        panel.Children.Add(scanRow);
        panel.Children.Add(new TextBlock { Text = Texts.Get("BillingKeyboardHelp"), ToolTip = Texts.Get("BillingKeyboardHelp"), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis });
        _billingBarcode = Field(barcodeField, "ScanBarcode");
        var scanBox = _billingBarcode;
        var scanQuantity = Field(weightField, "ScanQuantity", "1");
        ((TextBlock)barcodeField.Children[0]).Text = Texts.Get("CheckoutBarcode");
        ((TextBlock)weightField.Children[0]).Text = Texts.Get("CheckoutQuantity");
        foreach (var field in new[] { barcodeField, weightField })
            ((TextBlock)field.Children[0]).Margin = new Thickness(0, 0, 0, 2);
        async Task EnqueueScan()
        {
            if (string.IsNullOrWhiteSpace(scanBox.Text)) return;
            if (!decimal.TryParse(scanQuantity.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var quantity))
            { _vm.ShowMessage("InvalidBillingQuantity"); return; }
            var code = scanBox.Text;
            scanBox.Clear();
            _billingScanQueue = QueueScanAsync(_billingScanQueue, code, quantity, _vm.Session.Version);
            await _billingScanQueue;
        }
        scanBox.KeyDown += async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; await EnqueueScan(); } };
        var addBarcode = new Button { Content = Texts.Get("CheckoutAdd"), ToolTip = Texts.Get("AddBarcode"), HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetAutomationId(addBarcode, "AddBarcode");
        AutomationProperties.SetName(addBarcode, Texts.Get("AddBarcode"));
        addBarcode.Click += async (_, _) => await EnqueueScan();
        addBarcode.MaxWidth = 128; addBarcode.VerticalAlignment = VerticalAlignment.Bottom;
        Grid.SetColumn(addBarcode, 2); scanRow.Children.Add(addBarcode);
        _billingActions = workspace.Actions; _billingActions.IsEnabled = !_vm.Busy;
        var actions = workspace.OptionsHost;
        var searchPanel = new StackPanel(); workspace.SearchHost.Content = searchPanel;
        var editor = new StackPanel(); workspace.LineHost.Content = editor;
        actions.Children.Add(Button("RefreshBilling", () => _vm.NavigateAsync("Billing")));
        if (state.Shift is null)
        {
            actions.Children.Add(new TextBlock { Text = Texts.Get("OpenShiftRequired"), TextWrapping = TextWrapping.Wrap });
            var opening = Field(actions, "OpeningFloat", "0");
            var operation = Guid.NewGuid();
            actions.Children.Add(Button("OpenShift", async () =>
            {
                RequireScansFinished();
                if (!decimal.TryParse(opening.Text, out var amount)) throw new FoundationException("InvalidFloat");
                await _vm.Billing.OpenShiftAsync(operation, amount);
                await _vm.NavigateAsync("Billing");
                _vm.ShowSaved();
            }));
        }
        else actions.Children.Add(new TextBlock { Text = $"{Texts.Get("OpeningFloat")}: {state.Shift.OpeningFloat:0.00} · {Texts.Get("ExpectedDrawer")}: {state.Shift.ExpectedCash:0.00} {q.Currency}", TextWrapping = TextWrapping.Wrap });
        _billingSearchBox = Field(searchPanel, "BillingSearch", _billingSearch);
        var search = _billingSearchBox;
        var selection = new StackPanel();
        Label(selection, "Category");
        var categories = new ComboBox { DisplayMemberPath = nameof(BillingCategory.Display) };
        var categoryItems = new[] { new BillingCategory(default, Texts.Get("AllCategories")) }.Concat(state.Categories).ToArray();
        categories.ItemsSource = categoryItems;
        categories.SelectedItem = categoryItems.SingleOrDefault(c => c.Id == _billingCategory) ?? categoryItems[0];
        AutomationProperties.SetAutomationId(categories, "BillingCategory");
        selection.Children.Add(categories);
        var productsView = new ListCollectionView(state.Products.Where(p => p.Active).ToList());
        void Filter()
        {
            _billingSearch = search.Text;
            _billingCategory = categories.SelectedItem is BillingCategory c && c.Id != default ? c.Id : null;
            productsView.Filter = item => item is BillingProduct p && (_billingCategory is null || p.CategoryId == _billingCategory)
                && (p.Sku.Contains(search.Text.Trim(), StringComparison.CurrentCultureIgnoreCase)
                    || p.SinhalaName.Contains(search.Text.Trim(), StringComparison.CurrentCultureIgnoreCase)
                    || p.EnglishName.Contains(search.Text.Trim(), StringComparison.CurrentCultureIgnoreCase)
                    || p.Barcodes.Any(b => b.Contains(search.Text.Trim(), StringComparison.Ordinal)));
        }
        search.TextChanged += (_, _) => Filter(); categories.SelectionChanged += (_, _) => Filter(); Filter();
        var products = StockTable(productsView, "BillingProducts", ("Sku", "Sku", null), ("SinhalaName", "SinhalaName", null),
            ("EnglishName", "EnglishName", null), ("Unit", "Unit", "Unit"));
        products.Height = 130;
        selection.Children.Add(WithEmptyState(products));
        var productPicker = new Expander { Header = Texts.Get("Products"), Content = selection, IsExpanded = !string.IsNullOrWhiteSpace(_billingSearch) || _billingCategory is not null };
        AutomationProperties.SetAutomationId(productPicker, "BillingProductPicker");
        searchPanel.Children.Add(productPicker);
        search.TextChanged += (_, _) => productPicker.IsExpanded = search.Text.Length > 0;
        var quantityToAdd = Field(selection, "ProductQuantity", "1");
        selection.Children.Add(Button("AddSelectedProduct", async () =>
        {
            RequireScansFinished();
            if (products.SelectedItem is not BillingProduct p) throw new FoundationException("SelectProduct");
            if (!decimal.TryParse(quantityToAdd.Text, out var quantity)) throw new FoundationException("InvalidBillingQuantity");
            var previous = state.Basket.Items.SingleOrDefault(i => i.ProductId == p.Id);
            _vm.SetBillingState(await _vm.Billing.EditAsync(new BasketEdit(state.Basket.Id, state.Basket.Version, p.Id,
                quantity + (previous?.QuantityThousandths ?? 0) / 1000m, RequestedUnitPrice: previous?.RequestedUnitPriceMinor is { } minor
                    ? Money.FromMinor(minor, currency).ToDecimal() : null, Reason: previous?.Reason)));
        }));
        ArrangeFields(selection);
        var cart = StockTable(q.Lines, "BillingBasket", ("CheckoutItem", "ReceiptName", null),
            ("CheckoutQuantity", "Quantity", null), ("CheckoutPrice", "ChargedUnitPrice", null), ("PreviewTotal", "Total", null));
        cart.Style = (Style)FindResource("PosBillingBasket");
        var quantityDisplay = new MultiBinding { StringFormat = "{0:0.000} {1}", Mode = BindingMode.OneWay };
        quantityDisplay.Bindings.Add(new Binding(nameof(BillingLine.Quantity)) { Mode = BindingMode.OneWay });
        quantityDisplay.Bindings.Add(new Binding(nameof(BillingLine.Unit)) { Mode = BindingMode.OneWay, Converter = new EnumTextConverter("Unit") });
        ((DataGridTextColumn)cart.Columns[1]).Binding = quantityDisplay;
        ((DataGridTextColumn)cart.Columns[2]).Binding = new Binding(nameof(BillingLine.ChargedUnitPrice)) { Mode = BindingMode.OneWay, StringFormat = "{0:0.00}" };
        ((DataGridTextColumn)cart.Columns[3]).Binding = new Binding(nameof(BillingLine.Total)) { Mode = BindingMode.OneWay, StringFormat = "{0:0.00}" };
        cart.ClearValue(HeightProperty); cart.MinHeight = 72;
        // Set each column explicitly: auto-sized numeric columns must not squeeze the name column.
        foreach (var column in cart.Columns) column.Width = new DataGridLength(1, DataGridLengthUnitType.Star);
        cart.Columns[0].Width = new DataGridLength(2, DataGridLengthUnitType.Star);
        foreach (var column in cart.Columns.OfType<DataGridTextColumn>())
            column.ElementStyle = (Style)FindResource("PosWrappedCell");
        ScrollViewer.SetHorizontalScrollBarVisibility(cart, ScrollBarVisibility.Disabled);
        var basket = WithEmptyState(cart);
        if (q.Lines.Count == 0) ((TextBlock)basket.Children[1]).Text = Texts.Get("BasketEmpty");
        workspace.BasketHost.Content = basket;
        if (q.ErrorKey is { } error) actions.Children.Add(new TextBlock { Text = Texts.Get(error), Foreground = (System.Windows.Media.Brush)FindResource("PosError"), TextWrapping = TextWrapping.Wrap });
        var editQuantity = Field(editor, "BillingQuantity", "1");
        ((TextBlock)editor.Children[0]).Text = Texts.Get("CheckoutQuantity");
        editQuantity.Margin = new Thickness(0, 2, 0, 0);
        var lineDetails = new StackPanel();
        var finalPrice = Field(lineDetails, "RequestedFinalPrice");
        var discountReason = Field(lineDetails, "DiscountReason");
        lineDetails.Children.Add(new TextBlock { Text = Texts.Get("ManualPriceHelp"), TextWrapping = TextWrapping.Wrap });
        var selectedDetails = new TextBlock { TextWrapping = TextWrapping.Wrap };
        lineDetails.Children.Add(selectedDetails);
        var details = new Expander { Header = Texts.Get("LineDetails"), Content = lineDetails };
        AutomationProperties.SetAutomationId(details, "BillingLineDetails");
        actions.Children.Add(details);
        cart.SelectionChanged += (_, _) =>
        {
            if (cart.SelectedItem is not BillingLine line) return;
            _billingSelectedLine = line.ProductId;
            editQuantity.Text = line.Quantity.ToString(CultureInfo.CurrentCulture);
            finalPrice.Text = line.Manual ? line.ChargedUnitPrice.ToString(CultureInfo.CurrentCulture) : "";
            discountReason.Text = line.Reason ?? "";
            selectedDetails.Text = $"{line.Sku} · {line.SinhalaName} / {line.EnglishName}\n"
                + $"{Texts.Get("Unit")}: {Texts.Get("Unit" + line.Unit)} · {Texts.Get("NormalPrice")}: {line.NormalUnitPrice:0.00}\n"
                + $"{Texts.Get("TierPrice")}: {line.TierUnitPrice:0.00} · {Texts.Get("QualifiedBand")}: {line.TierBand}\n"
                + $"{Texts.Get("Savings")}: {line.Savings:0.00}";
        };
        cart.SelectedItem = q.Lines.SingleOrDefault(l => l.ProductId == _billingSelectedLine);
        editor.Children.Add(Button("UpdateBillingLine", async () =>
        {
            RequireScansFinished();
            if (cart.SelectedItem is not BillingLine line) throw new FoundationException("SelectBillingLine");
            if (!decimal.TryParse(editQuantity.Text, out var quantity)) throw new FoundationException("InvalidBillingQuantity");
            decimal? price = null;
            if (!string.IsNullOrWhiteSpace(finalPrice.Text))
            {
                if (!decimal.TryParse(finalPrice.Text, out var parsed)) throw new FoundationException("InvalidManualPrice");
                price = parsed;
            }
            _vm.SetBillingState(await _vm.Billing.EditAsync(new BasketEdit(state.Basket.Id, state.Basket.Version, line.ProductId, quantity,
                RequestedUnitPrice: price, Reason: discountReason.Text)));
        }));
        _billingRemove = Button("RemoveBillingLine", async () =>
        {
            RequireScansFinished();
            if (cart.SelectedItem is not BillingLine line) throw new FoundationException("SelectBillingLine");
            _vm.SetBillingState(await _vm.Billing.EditAsync(new BasketEdit(state.Basket.Id, state.Basket.Version, line.ProductId, 0, Remove: true)));
        });
        editor.Children.Add(_billingRemove);
        var editRow = new WrapPanel();
        var quantityField = new StackPanel { Width = 84, Margin = new Thickness(0, 0, 6, 0) };
        for (var i = 0; i < 2; i++) { var child = editor.Children[0]; editor.Children.RemoveAt(0); quantityField.Children.Add(child); }
        editRow.Children.Add(quantityField);
        while (editor.Children.Count > 0)
        {
            var child = editor.Children[0]; editor.Children.RemoveAt(0);
            if (child is Button button)
            {
                button.ToolTip = button.Content;
                button.Content = Texts.Get(AutomationProperties.GetAutomationId(button) == "RemoveBillingLine" ? "CheckoutRemove" : "CheckoutUpdate");
                button.MaxWidth = 110; button.VerticalAlignment = VerticalAlignment.Bottom;
            }
            editRow.Children.Add(child);
        }
        editor.Children.Add(editRow);
        actions.Children.Add(Button("ClearBasket", async () =>
        {
            RequireScansFinished(); _vm.SetBillingState(await _vm.Billing.ClearAsync(state.Basket.Id, state.Basket.Version));
        }));
        var total = new TextBlock { FontSize = 25, FontWeight = FontWeights.Bold,
            Text = $"{Texts.Get("PreviewTotal")}: {Amount(q.TotalMinor)} {q.Currency}", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        AutomationProperties.SetAutomationId(total, "BillingTotal"); workspace.TotalsHost.Children.Add(total);
        var method = new ComboBox { ItemsSource = new[] { Texts.Get("TenderCash"), Texts.Get("TenderCard") }, SelectedIndex = 0 };
        AutomationProperties.SetAutomationId(method, "PaymentMethod");
        AutomationProperties.SetName(method, Texts.Get("PaymentTitle")); workspace.TenderHost.Children.Add(method);
        var due = new TextBlock { TextWrapping = TextWrapping.Wrap }; workspace.TotalsHost.Children.Add(due);
        _billingTender = Field(workspace.TenderHost, "CashTendered", _billingTenderText); var tender = _billingTender;
        tender.TextChanged += (_, _) => _billingTenderText = tender.Text;
        var cashChange = new TextBlock { FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap }; workspace.TenderHost.Children.Add(cashChange);
        var cardApproved = new CheckBox { Content = Texts.Get("ExternalCardApproved") };
        AutomationProperties.SetAutomationId(cardApproved, "ExternalCardApproved"); actions.Children.Add(cardApproved);
        var cardReference = Field(actions, "ExternalCardReference");
        actions.Children.Add(new TextBlock { Text = Texts.Get("CardReferenceHelp"), TextWrapping = TextWrapping.Wrap });
        var negative = new CheckBox { Content = Texts.Get("AllowNegativeStock") };
        actions.Children.Add(negative);
        var negativeReason = Field(actions, "NegativeStockReason");
        string StockDescription(BillingStock stock)
        {
            var root = state.Products.Single(p => p.Id == stock.ProductId);
            return $"{root.SinhalaName} / {root.EnglishName} · {Texts.Get("StockBefore")}: {stock.Available:0.###} · "
                + $"{Texts.Get("StockConsumed")}: {stock.Required:0.###} · {Texts.Get("StockAfter")}: {stock.Remaining:0.###} {Texts.Get("Unit" + root.Unit)}";
        }
        if (q.NeedsStockApproval) actions.Children.Add(new TextBlock { Text = Texts.Get("InsufficientStock") + "\n"
            + string.Join('\n', q.Stock.Where(s => s.Insufficient).Select(StockDescription)),
            Foreground = System.Windows.Media.Brushes.DarkRed, TextWrapping = TextWrapping.Wrap });
        void TenderPreview()
        {
            var cash = method.SelectedIndex == 0;
            tender.IsEnabled = cash; cardApproved.IsEnabled = !cash; cardReference.IsEnabled = !cash;
            due.Text = cash ? $"{Texts.Get("CashRounding")}: {Amount(q.CashRoundingMinor)} · {Texts.Get("CashDue")}: {Amount(q.CashDueMinor)}"
                : $"{Texts.Get("CardDue")}: {Amount(q.TotalMinor)}";
            var cashDue = Money.FromMinor(q.CashDueMinor, currency).ToDecimal();
            cashChange.Text = cash && decimal.TryParse(tender.Text, out var value)
                ? value >= cashDue ? $"{Texts.Get("CashChange")}: {value - cashDue:0.00}"
                    : $"{Texts.Get("CashStillOwed")}: {cashDue - value:0.00}" : "";
        }
        method.SelectionChanged += (_, _) => TenderPreview(); tender.TextChanged += (_, _) => TenderPreview(); TenderPreview();
        _billingPay = Button("CompleteSale", async () =>
        {
            RequireScansFinished();
            var cash = method.SelectedIndex == 0;
            long tendered = q.TotalMinor;
            if (cash)
            {
                try { tendered = Money.FromMajor(decimal.Parse(tender.Text, CultureInfo.CurrentCulture), currency).MinorUnits; }
                catch (Exception ex) when (ex is FormatException or OverflowException or RetailCore.Domain.Common.DomainException) { throw new FoundationException("InvalidTender"); }
            }
            var request = new CompletionRequest(state.Basket.Id, state.Basket.Version, q.Hash, cash ? PaymentMethod.Cash : PaymentMethod.Card,
                tendered, !cash && cardApproved.IsChecked == true, cash ? null : cardReference.Text, negative.IsChecked == true, negativeReason.Text);
            ApprovalTicket? ticket = null;
            if (q.NeedsDiscountApproval || q.NeedsStockApproval)
            {
                var review = string.Join('\n', q.Lines.Select(l => $"{l.SinhalaName} / {l.EnglishName}: {l.Quantity:0.###} × {l.ChargedUnitPrice:0.00} = {l.Total:0.00} · "
                    + $"{Texts.Get("TierPrice")}: {l.TierUnitPrice:0.00} · {Texts.Get("DiscountReason")}: {l.Reason}"))
                    + $"\n{total.Text}\n{due.Text}\n{Texts.Get("CashTendered")}: {Money.FromMinor(tendered, currency).ToDecimal():0.00}\n"
                    + string.Join('\n', q.Stock.Select(StockDescription))
                    + $"\n{Texts.Get("NegativeStockReason")}: {request.NegativeReason}\n{Texts.Get("ManualPriceHelp")}";
                var dialog = new ApprovalDialog(_vm.Service, _vm.Approvers, RetailCore.Application.Checkout.BillingService.CompletionApproval(request), review) { Owner = this };
                if (dialog.ShowDialog() != true) return;
                ticket = dialog.Ticket;
            }
            await _vm.Billing.CompleteAsync(request, ticket);
            _billingTenderText = "0";
            _vm.SetBillingState(await _vm.Billing.LoadAsync());
        });
        _billingPay.IsEnabled = state.Shift is not null && q.Lines.Count > 0 && q.ErrorKey is null;
        _billingPay.Content = Texts.Get("CheckoutComplete");
        _billingPay.ToolTip = Texts.Get("CompleteSale");
        _billingPay.HorizontalAlignment = HorizontalAlignment.Stretch;
        _billingPay.Margin = new Thickness(0, 8, 0, 0); _billingPay.MinHeight = 44;
        workspace.CheckoutHost.Content = _billingPay;
        if (state.Basket.LastResult is { } last)
        {
            var saved = new TextBlock { Text = $"{Texts.Get("SavedSale")}: {last.Receipt} · {Texts.Get(last.Method == PaymentMethod.Cash ? "TenderCash" : "TenderCard")}\n"
                    + $"{Texts.Get("PreviewTotal")}: {Amount(last.TotalMinor)} · {Texts.Get("CashRounding")}: {Amount(last.RoundingMinor)} · "
                    + $"{Texts.Get("PaidAmount")}: {Amount(last.PaidMinor)} · {Texts.Get("CashChange")}: {Amount(last.ChangeMinor)} {last.Currency}",
                FontSize = 14, TextWrapping = TextWrapping.Wrap };
            AutomationProperties.SetAutomationId(saved, "SavedSaleResult"); actions.Children.Add(saved);
            actions.Children.Add(Button("OpenSavedBill", () => { RequireScansFinished(); return _vm.OpenBillAsync(last.SaleId); }));
            actions.Children.Add(Button("VerifyLastCompletion", async () =>
            {
                RequireScansFinished();
                await _vm.Billing.CompleteAsync(state.Basket.LastRequest!);
                _vm.SetBillingState(await _vm.Billing.LoadAsync()); _vm.ShowSaved();
            }));
        }
        panel.Loaded += (_, _) => scanBox.Focus();
        return workspace;
    }
}
