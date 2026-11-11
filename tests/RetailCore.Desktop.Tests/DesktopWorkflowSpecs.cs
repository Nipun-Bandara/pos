using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using RetailCore.Application.Foundation;
using RetailCore.Desktop;
using RetailCore.Infrastructure.Sqlite.Foundation;
using Shouldly;
using Xunit;

namespace RetailCore.Desktop.Tests;

/// <summary>Real WPF controls/dispatcher on Windows, using a disposable file database.</summary>
public sealed class DesktopWorkflowSpecs
{
    [Fact]
    public async Task Setup_sign_in_language_lock_and_restart_use_the_real_WPF_views()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.BeginInvoke(async () =>
            {
                try { await ExerciseAsync(); completion.TrySetResult(); }
                catch (Exception ex) { completion.TrySetException(ex); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromMinutes(3));
    }

    private static async Task ExerciseAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), "retailcore-wpf-" + Guid.NewGuid().ToString("N"));
        var application = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        application.InitializeComponent();
        Texts.Use("en");
        var services = new ServiceCollection().AddLocalFoundation(Path.Combine(path, "till.db"));
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var vm = new MainViewModel(provider.GetRequiredService<FoundationService>(), provider.GetRequiredService<LocalSession>(), provider.GetRequiredService<RetailCore.Application.Catalogue.CatalogueService>(), provider.GetRequiredService<RetailCore.Application.Inventory.StockService>(), provider.GetRequiredService<RetailCore.Application.Checkout.BillingService>(), provider.GetRequiredService<RetailCore.Application.Receipts.ReceiptService>());
        var window = new MainWindow(vm);
        try
        {
            window.Show();
            await UntilAsync(() => vm.Screen == "Setup" && !vm.Busy);
            Capture(window, "setup-en.png");
            Find<TextBox>(window, "ShopName").Text = "අපේ කඩේ";
            Find<TextBox>(window, "LegalName").Text = "Test shop";
            Find<TextBox>(window, "OwnerName").Text = "Owner";
            Find<PasswordBox>(window, "Pin").Password = "638194";
            Find<PasswordBox>(window, "ConfirmPin").Password = "638194";
            Click(window, "CreateShop");
            await UntilAsync(() => vm.Screen == "Login" && !vm.Busy);
            Find<PasswordBox>(window, "Pin").Password = "638194";
            Click(window, "SignIn");
            await UntilAsync(() => vm.Screen == "Shell" && !vm.Busy);
            await ExerciseProductsAsync(window, vm);
            await ExerciseStockAsync(window, vm);
            await ExerciseBillingAsync(window, vm);
            await ExerciseReceiptsAsync(window, vm);
            var language = Descendants(window).OfType<ComboBox>()
                .Single(c => AutomationProperties.GetName(c) == Texts.Get("Language"));
            language.SelectedIndex = 1;
            await UntilAsync(() => Texts.Language == "si" && !vm.Busy);
            Capture(window, "shell-si.png");
            Click(window, "Settings");
            await UntilAsync(() => vm.Section == "Settings" && !vm.Busy);
            Find<TextBox>(window, "ShopName").Text.ShouldBe("අපේ කඩේ");
            Find<TextBox>(window, "Telephone").Text = "0771234567";
            // The nested modal dispatcher allows the queued owner interaction to run during ShowDialog.
            var approvalInteraction = window.Dispatcher.InvokeAsync(async () =>
            {
                await UntilAsync(() => window.OwnedWindows.OfType<ApprovalDialog>().Any());
                var dialog = window.OwnedWindows.OfType<ApprovalDialog>().Single();
                Find<PasswordBox>(dialog, "Pin").Password = "638194";
                Click(dialog, "Approve");
            }).Task.Unwrap();
            Click(window, "SaveSettings");
            await approvalInteraction;
            await UntilAsync(() => !vm.Busy);
            vm.Profile!.Telephone.ShouldBe("0771234567");
            Find<TextBox>(window, "UserName").Text = "Cashier";
            Find<PasswordBox>(window, "Pin").Password = "123789";
            Find<PasswordBox>(window, "ConfirmPin").Password = "123789";
            Click(window, "CreateUser");
            await UntilAsync(() => !vm.Busy);
            vm.Users.Count.ShouldBe(2);
            Click(window, "Lock");
            vm.Session.IsLocked.ShouldBeTrue();
            vm.Screen.ShouldBe("Login");
            Find<PasswordBox>(window, "Pin").Password = "638194";
            Click(window, "Unlock");
            await UntilAsync(() => vm.Screen == "Shell" && !vm.Busy);
            var ownerId = vm.Current!.Id;
            window.Close();
            // Reopen the actual window and sign in against the same database with a new session.
            vm = new MainViewModel(provider.GetRequiredService<FoundationService>(), provider.GetRequiredService<LocalSession>(), provider.GetRequiredService<RetailCore.Application.Catalogue.CatalogueService>(), provider.GetRequiredService<RetailCore.Application.Inventory.StockService>(), provider.GetRequiredService<RetailCore.Application.Checkout.BillingService>(), provider.GetRequiredService<RetailCore.Application.Receipts.ReceiptService>());
            window = new MainWindow(vm);
            window.Show();
            await UntilAsync(() => vm.Screen == "Login" && !vm.Busy);
            var users = Find<ComboBox>(window, "LoginUser");
            users.SelectedItem = vm.Startup!.Users.Single(u => u.Id == ownerId);
            Find<PasswordBox>(window, "Pin").Password = "638194";
            Click(window, "SignIn");
            await UntilAsync(() => vm.Screen == "Shell" && !vm.Busy);
            Texts.Language.ShouldBe("si");
            vm.BillingState!.Basket.LastResult!.Receipt.ShouldBe("T1-000004");
            Click(window, "VerifyLastCompletion"); await UntilAsync(() => !vm.Busy);
            vm.BillingState!.Basket.LastResult!.Receipt.ShouldBe("T1-000004");
            Capture(window, "billing-si.png");
            Click(window, "OpenSavedBill"); await UntilAsync(() => vm.SelectedBill is not null && !vm.Busy);
            vm.SelectedBill!.Receipt.Number.ShouldBe("T1-000004");
            vm.SelectedBill.Prints.Single().Outcome!.Status.ShouldBe("PrintFailed");
            Capture(window, "receipt-history-si.png");
            vm.Current!.Language.ShouldBe("si");
            (await vm.Service.ProfileAsync()).Telephone.ShouldBe("0771234567");
            Click(window, "Products");
            await UntilAsync(() => vm.Section == "Products" && !vm.Busy);
            var restored = (await vm.Catalogue.LoadAsync()).Products.Single(p => p.Sku == "TEA-01");
            restored.SinhalaName.ShouldBe("තේ");
            restored.Barcode.ShouldBe("001234567890");
            Capture(window, "products-si.png");
            Click(window, "Stock"); await UntilAsync(() => vm.Section == "Stock" && !vm.Busy);
            (await vm.Stock.LoadAsync()).Products.Single(p => p.Sku == "RICE").Balance.ShouldBe(47.50m);
            Capture(window, "stock-si.png");
            Click(window, "SignOut");
            await UntilAsync(() => vm.Screen == "Login" && !vm.Busy);
            users = Find<ComboBox>(window, "LoginUser");
            users.SelectedItem = vm.Startup!.Users.Single(u => u.Name == "Cashier");
            Find<PasswordBox>(window, "Pin").Password = "123789";
            Click(window, "SignIn");
            await UntilAsync(() => vm.Screen == "Shell" && !vm.Busy);
            Click(window, "Settings");
            await UntilAsync(() => vm.Section == "Settings" && !vm.Busy);
            Descendants(window).OfType<Button>().Any(b => AutomationProperties.GetAutomationId(b) == "SaveSettings").ShouldBeFalse();
            Capture(window, "cashier-settings-si.png");
            Click(window, "Products");
            await UntilAsync(() => vm.Section == "Products" && !vm.Busy);
            Find<Button>(window, "SaveProduct").IsEnabled.ShouldBeFalse();
            var productTable = Find<DataGrid>(window, "ProductsTable");
            productTable.SelectedItem = productTable.Items.Cast<RetailCore.Application.Catalogue.ProductRow>().Single(p => p.Sku == "TEA-01");
            Find<TextBox>(window, "PreviewQuantity").Text = "2";
            Find<TextBlock>(window, "PricePreview").Text.ShouldContain("180.00");
            Click(window, "Stock"); await UntilAsync(() => vm.Section == "Stock" && !vm.Busy);
            Find<Button>(window, "PostStockEntry").IsEnabled.ShouldBeFalse();
            Find<Button>(window, "SaveStockConfiguration").IsEnabled.ShouldBeFalse();
        }
        finally
        {
            window.Close();
            application.Shutdown();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(path, true);
        }
    }

    private static async Task ExerciseReceiptsAsync(Window window, MainViewModel vm)
    {
        var profile = (await vm.Service.ProfileAsync()) with { PrinterName = "RetailCore-Acceptance-Nonexistent-Queue", PaperWidthMillimeters = 72 };
        var approval = await vm.Service.ApproveAsync(vm.Current!.Id, "638194", FoundationService.SettingsApproval(profile));
        await vm.Service.SaveProfileAsync(profile, approval);
        Click(window, "OpenSavedBill"); await UntilAsync(() => vm.SelectedBill is not null && !vm.Busy);
        vm.SelectedBill!.Receipt.Number.ShouldBe("T1-000004");
        Find<TextBox>(window, "SavedReceiptText").Text.ShouldContain("තේ");
        var original = Find<TextBox>(window, "SavedReceiptText").Text;
        // Execute the real Windows adapter's missing-queue path. This is not a physical printer test.
        Click(window, "PrintReceipt"); await UntilAsync(() => !vm.Busy);
        vm.Message.ShouldBe(Texts.Get("PrintFailed"));
        vm.SelectedBill!.Prints.Single().Outcome!.Status.ShouldBe("PrintFailed");
        var closePreview = window.Dispatcher.InvokeAsync(async () =>
        {
            await UntilAsync(() => window.OwnedWindows.Cast<Window>().Any());
            var preview = window.OwnedWindows.Cast<Window>().Single();
            Capture(preview, "receipt-provisional-si.png"); preview.Close();
        }).Task.Unwrap();
        Click(window, "PreviewReceipt"); await closePreview; await UntilAsync(() => !vm.Busy);
        var count = (await vm.Receipts.HistoryAsync(new(vm.ReceiptSettings!.Today, vm.ReceiptSettings.Today))).Count;
        count.ShouldBe(4);
        Find<TextBox>(window, "BillNumberLookup").Text = "t1-4";
        Click(window, "FindBill"); await UntilAsync(() => !vm.Busy);
        vm.SelectedBill!.Receipt.Number.ShouldBe("T1-000004");
        Find<TextBox>(window, "SavedReceiptText").Text.ShouldContain("පිටපත");
        original.ShouldContain("160.00");
        var receipt = vm.SelectedBill.Receipt;
        var longReceipt = receipt with { Lines = Enumerable.Range(1,100).Select(i => receipt.Lines[0] with
            { Number = i, Name = string.Concat(Enumerable.Repeat("දිගු සිංහල භාණ්ඩ නාමය ",8)) }).ToArray() };
        var rendered = ReceiptRenderer.Render(longReceipt,48,true);
        rendered.Language.IetfLanguageTag.ShouldBe("si-LK");
        rendered.Blocks.OfType<System.Windows.Documents.Table>().ShouldContain(t => t.Columns.Count == 4);
        receipt.Version.ShouldBe(2);
        var paginated = ((System.Windows.Documents.IDocumentPaginatorSource)rendered).DocumentPaginator;
        paginated.ComputePageCount(); paginated.PageCount.ShouldBeGreaterThan(1);
        paginated.GetPage(0).Size.Width.ShouldBe(48d * 96 / 25.4, .01);
        Click(window, "Billing"); await UntilAsync(() => vm.Section == "Billing" && !vm.Busy);
    }

    private static async Task ExerciseBillingAsync(Window window, MainViewModel vm)
    {
        var tea = (await vm.Catalogue.LoadAsync()).Products.Single(p => p.Sku == "TEA-01").Product.Id!.Value;
        await vm.Stock.PostAsync(new RetailCore.Application.Inventory.StockEntryInput(Guid.NewGuid(), tea,
            RetailCore.Application.Inventory.StockEntryKind.Receiving, 20, 50, "Test receiving", DateTimeOffset.UtcNow));
        Click(window, "Billing"); await UntilAsync(() => vm.Section == "Billing" && !vm.Busy);
        Find<TextBox>(window, "OpeningFloat").Text = "1000";
        Click(window, "OpenShift"); await UntilAsync(() => !vm.Busy);
        vm.BillingState!.Shift.ShouldNotBeNull();
        async Task Scan(string barcode)
        {
            Find<TextBox>(window, "ScanBarcode").Text = barcode; Click(window, "AddBarcode");
            await UntilAsync(() => !vm.Busy);
        }
        foreach (var expected in new long[] { 10000, 18000, 25500, 32000, 40000 })
        {
            await Scan("001234567890"); vm.BillingState!.Quote.TotalMinor.ShouldBe(expected);
        }
        var cart = Find<DataGrid>(window, "BillingBasket"); cart.SelectedIndex = 0;
        Find<TextBox>(window, "BillingQuantity").Text = "2";
        Click(window, "UpdateBillingLine"); await UntilAsync(() => !vm.Busy);
        vm.BillingState!.Quote.TotalMinor.ShouldBe(18000);
        Find<TextBox>(window, "ScanQuantity").Text = "1.250";
        await Scan("00001"); vm.BillingState!.Quote.TotalMinor.ShouldBe(36750);
        Find<TextBox>(window, "CashTendered").Text = "400";
        Click(window, "CompleteSale"); await UntilAsync(() => !vm.Busy);
        vm.BillingState!.Basket.LastResult!.TotalMinor.ShouldBe(36750);
        vm.BillingState.Basket.LastResult.RoundingMinor.ShouldBe(50);
        vm.BillingState.Shift!.ExpectedCash.ShouldBe(1368);
        await Scan("00001"); vm.BillingState!.Quote.TotalMinor.ShouldBe(15000);
        Find<TextBox>(window, "CashTendered").Text = "200";
        Click(window, "CompleteSale"); await UntilAsync(() => !vm.Busy);
        vm.BillingState!.Basket.LastResult!.ChangeMinor.ShouldBe(5000);
        vm.BillingState.Shift!.ExpectedCash.ShouldBe(1518);
        Click(window, "VerifyLastCompletion"); await UntilAsync(() => !vm.Busy);
        vm.BillingState!.Basket.LastResult!.Receipt.ShouldBe("T1-000002");
        await Scan("001234567890");
        Find<ComboBox>(window, "PaymentMethod").SelectedIndex = 1;
        Find<CheckBox>(window, "ExternalCardApproved").IsChecked = true;
        Find<TextBox>(window, "ExternalCardReference").Text = "AUTH-4471";
        Click(window, "CompleteSale"); await UntilAsync(() => !vm.Busy);
        vm.BillingState!.Basket.LastResult!.Receipt.ShouldBe("T1-000003");
        vm.BillingState.Shift!.ExpectedCash.ShouldBe(1518);
        await Scan("001234567890"); await Scan("001234567890");
        Find<DataGrid>(window, "BillingBasket").SelectedIndex = 0;
        Find<TextBox>(window, "RequestedFinalPrice").Text = "80";
        Find<TextBox>(window, "DiscountReason").Text = "Owner offer";
        Click(window, "UpdateBillingLine"); await UntilAsync(() => !vm.Busy);
        vm.BillingState!.Quote.TotalMinor.ShouldBe(16000);
        Find<TextBox>(window, "CashTendered").Text = "200";
        var approvalInteraction = window.Dispatcher.InvokeAsync(async () =>
        {
            await UntilAsync(() => window.OwnedWindows.OfType<ApprovalDialog>().Any());
            var dialog = window.OwnedWindows.OfType<ApprovalDialog>().Single();
            Find<PasswordBox>(dialog, "Pin").Password = "638194"; Click(dialog, "Approve");
        }).Task.Unwrap();
        Click(window, "CompleteSale"); await approvalInteraction; await UntilAsync(() => !vm.Busy);
        vm.BillingState!.Basket.LastResult!.Receipt.ShouldBe("T1-000004");
        vm.BillingState.Basket.LastResult.ChangeMinor.ShouldBe(4000);
        vm.BillingState.Shift!.ExpectedCash.ShouldBe(1678);
        Capture(window, "billing-en.png");
    }

    private static async Task ExerciseStockAsync(Window window, MainViewModel vm)
    {
        var category = await vm.Catalogue.SaveCategoryAsync(null, "සහල්", "Rice", true);
        var root = await vm.Catalogue.SaveProductAsync(new RetailCore.Application.Catalogue.ProductInput(null,
            "RICE", "සහල්", "Loose rice", "සහල්", category, RetailCore.Domain.Catalogue.UnitOfMeasure.Kilogram,
            100, 150, "00001", []));
        var pack = await vm.Catalogue.SaveProductAsync(new RetailCore.Application.Catalogue.ProductInput(null,
            "RICE-25", "සහල් බෑගය", "Rice bag", "සහල් බෑගය", category, RetailCore.Domain.Catalogue.UnitOfMeasure.Pack,
            2500, 3000, "00002", []));
        Click(window, "Stock");
        await UntilAsync(() => vm.Section == "Stock" && !vm.Busy);
        void Select(RetailCore.Domain.Common.ProductId id)
        {
            var product = Find<ComboBox>(window, "StockProduct");
            product.SelectedItem = product.Items.Cast<RetailCore.Application.Inventory.StockRow>().Single(p => p.Id == id);
        }
        Select(root);
        Find<TextBox>(window, "Reorder").Text = "10";
        Click(window, "SaveStockConfiguration"); await UntilAsync(() => !vm.Busy);
        Select(pack);
        var baseProduct = Find<ComboBox>(window, "StockBaseProduct");
        baseProduct.SelectedItem = baseProduct.Items.Cast<RetailCore.Application.Inventory.StockRow>().Single(p => p.Id == root);
        Find<TextBox>(window, "StockFactor").Text = "25";
        Click(window, "SaveStockConfiguration"); await UntilAsync(() => !vm.Busy);
        vm.Message.ShouldBe(Texts.Get("Saved"));
        Select(pack);
        Find<TextBox>(window, "EnteredQuantity").Text = "2";
        Find<TextBox>(window, "StockInputCost").Text = "2500";
        Find<TextBox>(window, "StockReason").Text = "Delivery / ලැබීම";
        Click(window, "PostStockEntry"); await UntilAsync(() => !vm.Busy);
        vm.Message.ShouldBe(Texts.Get("Saved"));
        Select(root);
        Find<TextBlock>(window, "SelectedStockBalance").Text.ShouldContain("50");
        var packChoice = Find<ComboBox>(window, "ConsumptionPack");
        packChoice.SelectedItem = packChoice.Items.Cast<RetailCore.Application.Inventory.StockRow>().Single(p => p.Id == pack);
        Find<TextBox>(window, "ConsumptionQuantity").Text = "1.250";
        Find<TextBox>(window, "ConsumptionPackQuantity").Text = "1";
        Click(window, "PreviewConsumption"); await UntilAsync(() => !vm.Busy);
        Find<TextBlock>(window, "ConsumptionResult").Text.ShouldContain("23.75");
        Find<DataGrid>(window, "StockHistory").Items.Count.ShouldBe(1);
        Find<ComboBox>(window, "StockEntryKind").SelectedIndex = 4;
        Find<TextBox>(window, "EnteredQuantity").Text = "0.250";
        Find<TextBox>(window, "StockInputCost").Text = "100";
        Find<TextBox>(window, "StockReason").Text = "Damaged rice";
        var approvalInteraction = window.Dispatcher.InvokeAsync(async () =>
        {
            await UntilAsync(() => window.OwnedWindows.OfType<ApprovalDialog>().Any());
            var dialog = window.OwnedWindows.OfType<ApprovalDialog>().Single();
            Find<PasswordBox>(dialog, "Pin").Password = "638194";
            Click(dialog, "Approve");
        }).Task.Unwrap();
        Click(window, "PostStockEntry"); await approvalInteraction; await UntilAsync(() => !vm.Busy);
        vm.Message.ShouldBe(Texts.Get("Saved")); Select(root);
        Find<TextBlock>(window, "SelectedStockBalance").Text.ShouldContain("49.75");
        Find<DataGrid>(window, "StockHistory").Items.Count.ShouldBe(2);
        Capture(window, "stock-en.png");
    }

    private static async Task ExerciseProductsAsync(Window window, MainViewModel vm)
    {
        Click(window, "Products");
        await UntilAsync(() => vm.Section == "Products" && !vm.Busy);
        Find<TextBox>(window, "CategorySinhala").Text = "පාන";
        Find<TextBox>(window, "CategoryEnglish").Text = "Drinks";
        Click(window, "SaveCategory");
        await UntilAsync(() => !vm.Busy);
        Find<TextBox>(window, "Sku").Text = "TEA-01";
        Find<TextBox>(window, "SinhalaName").Text = "තේ";
        Find<TextBox>(window, "EnglishName").Text = "Tea";
        Find<TextBox>(window, "ReceiptName").Text = "තේ පැකට්";
        Find<TextBox>(window, "Barcode").Text = "001234567890";
        Find<TextBox>(window, "Cost").Text = "50";
        Find<TextBox>(window, "NormalPrice").Text = "100";
        for (var i = 1; i <= 3; i++)
        {
            Find<TextBox>(window, "Minimum" + i).Text = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            Find<TextBox>(window, "BandPrice" + i).Text = new[] { "90", "85", "80" }[i - 1];
        }
        foreach (var (quantity, total) in new[] { ("1", "100.00"), ("2", "180.00"), ("3", "255.00"),
            ("4", "320.00"), ("5", "400.00"), ("2", "180.00") })
        {
            Find<TextBox>(window, "PreviewQuantity").Text = quantity;
            Find<TextBlock>(window, "PricePreview").Text.ShouldContain(total);
        }
        Find<TextBox>(window, "Minimum2").Text = "1";
        Click(window, "SaveProduct");
        await UntilAsync(() => !vm.Busy);
        vm.Message.ShouldBe(Texts.Get("InvalidPricing"));
        Find<TextBox>(window, "Minimum2").Text = "3";
        Click(window, "SaveProduct");
        await UntilAsync(() => !vm.Busy);
        vm.Message.ShouldBe(Texts.Get("Saved"));
        var table = Find<DataGrid>(window, "ProductsTable");
        table.Items.Count.ShouldBe(1);
        Find<TextBox>(window, "ProductSearch").Text = "missing";
        table.Items.Count.ShouldBe(0);
        Find<TextBox>(window, "ProductSearch").Text = "තේ";
        table.Items.Count.ShouldBe(1);
        table.SelectedIndex = 0;
        Find<TextBox>(window, "Sku").Text.ShouldBe("TEA-01");
        Capture(window, "products-en.png");
    }

    private static void Click(DependencyObject root, string id) =>
        Find<Button>(root, id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static T Find<T>(DependencyObject root, string id) where T : DependencyObject =>
        Descendants(root).OfType<T>().Single(c => AutomationProperties.GetAutomationId(c) == id);

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in Descendants(child)) { yield return descendant; }
        }
    }

    private static async Task UntilAsync(Func<bool> predicate)
    {
        var limit = DateTimeOffset.UtcNow.AddSeconds(20);
        while (!predicate())
        {
            if (DateTimeOffset.UtcNow >= limit) { throw new TimeoutException("WPF workflow did not reach its expected state."); }
            await Task.Delay(25);
        }

        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    }

    private static void Capture(Window window, string name)
    {
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var directory = Path.Combine(AppContext.BaseDirectory, "artifacts");
        Directory.CreateDirectory(directory);
        using var output = File.Create(Path.Combine(directory, name));
        encoder.Save(output);
    }
}
