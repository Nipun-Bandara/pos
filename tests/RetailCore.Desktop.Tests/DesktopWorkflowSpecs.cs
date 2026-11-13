using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Input;
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
            try
            {
                dispatcher.BeginInvoke(async () =>
                {
                    try { await ExerciseAsync(); completion.TrySetResult(); }
                    catch (Exception ex) { completion.TrySetException(ex); }
                    finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
                });
                Dispatcher.Run();
            }
            catch (Exception ex)
            {
                // Layout/binding failures can occur outside ExerciseAsync's continuation.
                // Report their original exception instead of waiting for the outer timeout.
                completion.TrySetException(ex);
            }
            finally
            {
                completion.TrySetException(new InvalidOperationException("The WPF dispatcher exited before the workflow completed."));
            }
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
        var vm = new MainViewModel(provider.GetRequiredService<FoundationService>(), provider.GetRequiredService<LocalSession>(), provider.GetRequiredService<RetailCore.Application.Catalogue.CatalogueService>(), provider.GetRequiredService<RetailCore.Application.Inventory.StockService>(), provider.GetRequiredService<RetailCore.Application.Checkout.BillingService>(), provider.GetRequiredService<RetailCore.Application.Receipts.ReceiptService>(), provider.GetRequiredService<RetailCore.Application.Returns.ReturnService>(), provider.GetRequiredService<RetailCore.Application.Reporting.CashService>(), provider.GetRequiredService<RetailCore.Application.Reporting.ReportService>(), provider.GetRequiredService<RetailCore.Application.Recovery.RecoveryService>());
        var window = new MainWindow(vm, new DesktopAppearance(Path.Combine(path, "theme.txt")));
        try
        {
            window.Show();
            await UntilAsync(() => vm.Screen == "Setup" && !vm.Busy);
            AssertInClient(window, Find<ComboBox>(window, "UiLanguage"));
            AssertInClient(window, Find<Button>(window, "ThemeToggle"));
            window.Top.ShouldBeGreaterThanOrEqualTo(SystemParameters.WorkArea.Top);
            window.Top.ShouldBeLessThan(SystemParameters.WorkArea.Bottom);
            Capture(window, "setup-en.png");
            await CaptureAuthenticationSizesAsync(window, vm, "setup");
            Find<TextBox>(window, "ShopName").Text = "අපේ කඩේ";
            Find<TextBox>(window, "LegalName").Text = "Test shop";
            Find<TextBox>(window, "OwnerName").Text = "Owner";
            Find<PasswordBox>(window, "Pin").Password = "638194";
            Find<PasswordBox>(window, "ConfirmPin").Password = "638194";
            Click(window, "CreateShop");
            await UntilAsync(() => vm.Screen == "Login" && !vm.Busy);
            await CaptureAuthenticationSizesAsync(window, vm, "login");
            Find<PasswordBox>(window, "Pin").Password = "638194";
            Click(window, "SignIn");
            await UntilAsync(() => vm.Screen == "Shell" && !vm.Busy);
            window.WindowStyle.ShouldBe(WindowStyle.SingleBorderWindow);
            window.ResizeMode.ShouldBe(ResizeMode.CanResize);
            AssertInClient(window, Find<ComboBox>(window, "UiLanguage"));
            var billingView = Find<BillingWorkspace>(window, "BillingWorkspace");
            // Re-loading an already authenticated window must not restart the login workflow.
            window.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            vm.Screen.ShouldBe("Shell");
            Find<BillingWorkspace>(window, "BillingWorkspace").ShouldBeSameAs(billingView);
            Descendants(window).OfType<ComboBox>().Any(c => AutomationProperties.GetAutomationId(c) == "LoginUser").ShouldBeFalse();
            Find<TextBox>(window, "ScanBarcode").Text = "000-PENDING";
            var lightColor = ((SolidColorBrush)window.FindResource("PosBackground")).Color;
            Click(window, "ThemeToggle"); await UntilAsync(() => !vm.Busy);
            new DesktopAppearance(Path.Combine(path, "theme.txt")).IsDark.ShouldBeTrue();
            ((SolidColorBrush)window.FindResource("PosBackground")).Color.ShouldNotBe(lightColor);
            vm.Screen.ShouldBe("Shell");
            Find<BillingWorkspace>(window, "BillingWorkspace").ShouldBeSameAs(billingView);
            Find<TextBox>(window, "ScanBarcode").Text.ShouldBe("000-PENDING");
            Find<TextBox>(window, "ScanBarcode").Clear();
            Capture(window, "billing-dark.png");
            Click(window, "ThemeToggle"); await UntilAsync(() => !vm.Busy);
            ((SolidColorBrush)window.FindResource("PosBackground")).Color.ShouldBe(lightColor);
            await ExerciseProductsAsync(window, vm);
            await ExerciseStockAsync(window, vm);
            await ExerciseBillingAsync(window, vm);
            await ExerciseReceiptsAsync(window, vm);
            await ExerciseReturnsAsync(window, vm);
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
                Capture(dialog, $"modern-approval-{Texts.Language}.png");
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
            await UntilAsync(() => vm.Screen == "Login" && !vm.Busy);
            await CaptureAuthenticationSizesAsync(window, vm, "locked");
            var lockedUser = Find<ComboBox>(window, "LoginUser");
            lockedUser.IsEnabled.ShouldBeFalse();
            ((UserSummary)lockedUser.SelectedItem).Id.ShouldBe(vm.Session.UserId!.Value);
            Descendants(window).OfType<TextBox>()
                .Any(c => AutomationProperties.GetAutomationId(c) == "ShopName").ShouldBeFalse();
            Find<PasswordBox>(window, "Pin").Password = "638194";
            Click(window, "Unlock");
            await UntilAsync(() => vm.Screen == "Shell" && !vm.Busy);
            var ownerId = vm.Current!.Id;
            window.Close();
            // Reopen the actual window and sign in against the same database with a new session.
            vm = new MainViewModel(provider.GetRequiredService<FoundationService>(), provider.GetRequiredService<LocalSession>(), provider.GetRequiredService<RetailCore.Application.Catalogue.CatalogueService>(), provider.GetRequiredService<RetailCore.Application.Inventory.StockService>(), provider.GetRequiredService<RetailCore.Application.Checkout.BillingService>(), provider.GetRequiredService<RetailCore.Application.Receipts.ReceiptService>(), provider.GetRequiredService<RetailCore.Application.Returns.ReturnService>(), provider.GetRequiredService<RetailCore.Application.Reporting.CashService>(), provider.GetRequiredService<RetailCore.Application.Reporting.ReportService>(), provider.GetRequiredService<RetailCore.Application.Recovery.RecoveryService>());
            window = new MainWindow(vm, new DesktopAppearance(Path.Combine(path, "theme.txt")));
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
            vm.SelectedBill.Returns!.Single().CreditMinor.ShouldBe(8000);
            Capture(window, "receipt-history-si.png");
            vm.Current!.Language.ShouldBe("si");
            (await vm.Service.ProfileAsync()).Telephone.ShouldBe("0771234567");
            Click(window, "Products");
            await UntilAsync(() => vm.Section == "Products" && !vm.Busy);
            var import = Descendants(window).OfType<Expander>().Single(e => Equals(e.Header, Texts.Get("ImportProducts")));
            import.IsExpanded = true;
            var importPath = await FindVisibleAsync<TextBox>(window, "ImportFile");
            var productFile = Path.Combine(path, "products.csv");
            var template = System.Text.Encoding.UTF8.GetString(RetailCore.Application.Catalogue.ProductImportFile.Template(".csv"));
            await File.WriteAllTextAsync(productFile, template.Replace("001234567890", "000555", StringComparison.Ordinal));
            importPath.Text = productFile;
            Click(window, "PreviewImport"); await UntilAsync(() => !vm.Busy);
            Find<DataGrid>(window, "ImportPreviewTable").Items.Count.ShouldBe(1);
            Find<Button>(window, "ImportProducts").IsEnabled.ShouldBeTrue();
            Capture(window, "product-import-si.png");
            Click(window, "ImportProducts"); await UntilAsync(() => !vm.Busy);
            vm.Message.ShouldBe(Texts.Get("ProductsImported"));
            (await vm.Catalogue.LoadAsync()).Products.Single(p => p.Sku == "TEA-001").Barcode.ShouldBe("000555");
            var restored = (await vm.Catalogue.LoadAsync()).Products.Single(p => p.Sku == "TEA-01");
            restored.SinhalaName.ShouldBe("තේ");
            restored.Barcode.ShouldBe("001234567890");
            Capture(window, "products-si.png");
            Click(window, "Stock"); await UntilAsync(() => vm.Section == "Stock" && !vm.Busy);
            (await vm.Stock.LoadAsync()).Products.Single(p => p.Sku == "RICE").Balance.ShouldBe(47.50m);
            Capture(window, "stock-si.png");
            await ExerciseReportsAsync(window,vm);
            await ExerciseRecoveryAsync(window,vm);
            await ReviewScreenLayoutsAsync(window, vm);
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

    private static async Task ExerciseRecoveryAsync(Window window,MainViewModel vm)
    {
        var owner=vm.Current!.Id;var review=await vm.Cash.ReviewAsync();
        var close=new RetailCore.Application.Reporting.CashRequest(Guid.NewGuid(),review.ShiftId,true,review.ExpectedMinor,false,"Windows recovery rehearsal",review.ExpectedMinor);
        await vm.Cash.PostAsync(close,await vm.Service.ApproveAsync(owner,"638194",RetailCore.Application.Reporting.CashService.Approval(close)));
        Click(window,"Settings");await UntilAsync(()=>vm.Section=="Settings" && !vm.Busy);
        Click(window,"RefreshBackup");await UntilAsync(()=>!vm.Busy);Click(window,"BackupNow");await UntilAsync(()=>!vm.Busy);
        var backup=await vm.Recovery.StatusAsync();backup.State.ShouldBe("BackupPrimaryOnly");
        Find<TextBox>(window,"RestoreFile").Text=backup.LastFile!;Find<TextBox>(window,"RestoreReason").Text="Windows verified restore rehearsal";
        Click(window,"ReviewRestore");await UntilAsync(()=>!vm.Busy);
        vm.Message.ShouldBe(string.Empty, "backup verification must succeed before requesting restore approval");
        var restoreSummary = Find<TextBlock>(window, "RestoreReviewSummary").Text;
        restoreSummary.ShouldContain(vm.Profile!.Name);
        restoreSummary.ShouldContain(Texts.Get("RestoreChecksum"));
        Capture(window,"backup-restore-si.png");
        var approval=window.Dispatcher.InvokeAsync(async()=>
        {
            await UntilAsync(() =>
            {
                if (!string.IsNullOrEmpty(vm.Message))
                    throw new InvalidOperationException($"Restore approval did not open: {vm.Message}");
                return window.OwnedWindows.OfType<ApprovalDialog>().Any();
            });
            var dialog=window.OwnedWindows.OfType<ApprovalDialog>().Single();
            Find<PasswordBox>(dialog,"Pin").Password="638194";Click(dialog,"Approve");
        }).Task.Unwrap();
        Click(window,"RestoreBackup");await approval;await UntilAsync(()=>vm.Screen=="Login" && !vm.Busy);
        var restoredUsers = Find<ComboBox>(window, "LoginUser");
        restoredUsers.SelectedItem = vm.Startup!.Users.Single(user => user.Id == owner);
        Find<PasswordBox>(window,"Pin").Password="638194";Click(window,"SignIn");await UntilAsync(()=>vm.Screen=="Shell" && !vm.Busy);
        (await vm.Billing.LoadAsync()).Shift.ShouldBeNull();(await vm.Stock.LoadAsync()).Products.Single(p=>p.Sku=="RICE").Balance.ShouldBe(47.50m);
    }

    private static async Task ExerciseReportsAsync(Window window,MainViewModel vm)
    {
        Click(window,"Reports");await UntilAsync(()=>vm.Section=="Reports" && !vm.Busy);
        var tabs=Descendants(window).OfType<TabControl>().Single();tabs.SelectedIndex=2;await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Find<ComboBox>(window,"ReportKind").SelectedIndex=0;Click(window,"LoadReport");await UntilAsync(()=>!vm.Busy);
        Find<DataGrid>(window,"TradingReportTable").Items.Count.ShouldBe(1);Capture(window,"trading-report-si.png");
        var table=new RetailCore.Application.Reporting.ReportTable("සිංහල වාර්තාව","2026-11-09 · Asia/Colombo · LKR",["කේතය","නම","මිල"],
            Enumerable.Range(0,80).Select(i=>new object?[]{"001234", "තේ පැකට් සහ දිගු සිංහල භාණ්ඩ නම "+i,100.25m}).ToArray());
        var bytes=ReportPdf.Render(table);System.Text.Encoding.ASCII.GetString(bytes.Take(8).ToArray()).ShouldBe("%PDF-1.4");
        var directory=Path.Combine(AppContext.BaseDirectory,"artifacts");Directory.CreateDirectory(directory);File.WriteAllBytes(Path.Combine(directory,"sinhala-report.pdf"),bytes);
        File.WriteAllBytes(Path.Combine(directory,"sinhala-report.xlsx"),RetailCore.Application.Reporting.TableExport.Xlsx(table));
        File.WriteAllBytes(Path.Combine(directory,"sinhala-report.csv"),RetailCore.Application.Reporting.TableExport.Csv(table));
    }

    private static async Task ExerciseReturnsAsync(Window window,MainViewModel vm)
    {
        Click(window,"Returns");await UntilAsync(()=>vm.Section=="Returns" && !vm.Busy);
        Find<TextBox>(window,"ReturnBillLookup").Text="T1-000004";Click(window,"FindReturnBill");await UntilAsync(()=>vm.ReturnBill is not null && !vm.Busy);
        var grid=Find<DataGrid>(window,"ReturnLines");grid.SelectedIndex=0;grid.CurrentCell=new DataGridCellInfo(grid.Items[0],grid.Columns[3]);
        grid.ScrollIntoView(grid.Items[0],grid.Columns[3]);grid.BeginEdit();window.UpdateLayout();
        Descendants(grid).OfType<TextBox>().Single().Text="1";grid.CommitEdit(DataGridEditingUnit.Cell,true);grid.CommitEdit(DataGridEditingUnit.Row,true);
        Find<TextBox>(window,"ReturnReason").Text="Customer returned one item";Click(window,"PreviewReturn");await UntilAsync(()=>!vm.Busy);
        var approval=window.Dispatcher.InvokeAsync(async()=>
        {
            await UntilAsync(()=>window.OwnedWindows.OfType<ApprovalDialog>().Any());var dialog=window.OwnedWindows.OfType<ApprovalDialog>().Single();
            Find<PasswordBox>(dialog,"Pin").Password="638194";Click(dialog,"Approve");
        }).Task.Unwrap();
        Click(window,"CompleteReturn");await approval;await UntilAsync(()=>!vm.Busy);
        vm.ReturnBill!.History.Single().CreditMinor.ShouldBe(8000);
        vm.ReturnBill.History.Single().Number.ShouldBe("T1-000005");
        (await vm.Billing.LoadAsync()).Shift!.ExpectedCash.ShouldBe(1598);
        Click(window,"PrintReturnReceipt");await UntilAsync(()=>!vm.Busy);vm.Message.ShouldBe(Texts.Get("PrintFailed"));
        var doc=await vm.Receipts.ReturnInfoAsync(vm.ReturnBill.History.Single().Id);doc.Document.Version.ShouldBe(3);doc.Prints.Single().Outcome!.Status.ShouldBe("PrintFailed");
        var closePreview=window.Dispatcher.InvokeAsync(async()=>
        {
            await UntilAsync(()=>window.OwnedWindows.Cast<Window>().Any());var preview=window.OwnedWindows.Cast<Window>().Single();
            Capture(preview,"return-receipt-si.png");preview.Close();
        }).Task.Unwrap();
        Click(window,"PreviewReturnReceipt");await closePreview;await UntilAsync(()=>!vm.Busy);
        Capture(window,"returns-linked.png");Click(window,"Billing");await UntilAsync(()=>vm.Section=="Billing" && !vm.Busy);
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
        Click(window, "ThemeToggle"); await UntilAsync(() => !vm.Busy);
        var rendered = ReceiptRenderer.Render(longReceipt,48,true);
        // WPF canonicalizes XmlLanguage tags to lowercase; compare language values, not casing.
        rendered.Foreground.ShouldBe(Brushes.Black); rendered.Background.ShouldBe(Brushes.White);
        rendered.Language.ShouldBe(System.Windows.Markup.XmlLanguage.GetLanguage("si-LK"));
        rendered.Blocks.OfType<System.Windows.Documents.Table>().ShouldContain(t => t.Columns.Count == 4);
        receipt.Version.ShouldBe(2);
        var paginated = ((System.Windows.Documents.IDocumentPaginatorSource)rendered).DocumentPaginator;
        paginated.ComputePageCount(); paginated.PageCount.ShouldBeGreaterThan(1);
        paginated.GetPage(0).Size.Width.ShouldBe(48d * 96 / 25.4, .01);
        Click(window, "ThemeToggle"); await UntilAsync(() => !vm.Busy);
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
            var version = vm.BillingState!.Basket.Version;
            var input = Find<TextBox>(window, "ScanBarcode"); input.Focus(); input.Text = barcode;
            Enter(input, window);
            await UntilAsync(() => !vm.Busy && vm.BillingState!.Basket.Version > version);
            Find<TextBox>(window, "ScanBarcode").IsKeyboardFocused.ShouldBeTrue();
        }
        var queuedVersion = vm.BillingState!.Basket.Version;
        for (var i = 0; i < 2; i++)
        {
            var input = Find<TextBox>(window, "ScanBarcode"); input.Text = "001234567890"; Enter(input, window);
        }
        await UntilAsync(() => !vm.Busy && vm.BillingState!.Basket.Version == queuedVersion + 2);
        vm.BillingState!.Quote.TotalMinor.ShouldBe(18000);
        Click(window, "ClearBasket"); await UntilAsync(() => !vm.Busy && vm.BillingState!.Basket.Items.Count == 0);
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
        await ReviewBillingSizesAsync(window, vm);
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
        var details = Find<Expander>(window, "BillingLineDetails"); details.IsExpanded = true;
        var finalPrice = await FindVisibleAsync<TextBox>(window, "RequestedFinalPrice");
        finalPrice.BringIntoView(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        finalPrice.Focus().ShouldBeTrue(); AssertInClient(window, finalPrice); finalPrice.Text = "80";
        var discountReason = await FindVisibleAsync<TextBox>(window, "DiscountReason");
        discountReason.BringIntoView(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        discountReason.Focus().ShouldBeTrue(); AssertInClient(window, discountReason); discountReason.Text = "Owner offer";
        Click(window, "UpdateBillingLine"); await UntilAsync(() => !vm.Busy);
        vm.BillingState!.Quote.TotalMinor.ShouldBe(16000);
        Find<TextBox>(window, "CashTendered").Text = "200";
        var approvalInteraction = window.Dispatcher.InvokeAsync(async () =>
        {
            await UntilAsync(() => window.OwnedWindows.OfType<ApprovalDialog>().Any());
            var dialog = window.OwnedWindows.OfType<ApprovalDialog>().Single();
            Capture(dialog, $"modern-approval-{Texts.Language}.png");
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
            Capture(dialog, $"modern-approval-{Texts.Language}.png");
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
        var activeColumn = table.Columns.OfType<DataGridCheckBoxColumn>().Single();
        ((Binding)activeColumn.Binding).Mode.ShouldBe(BindingMode.OneWay);
        activeColumn.IsReadOnly.ShouldBeTrue();
        table.ScrollIntoView(table.Items[0], activeColumn);
        table.UpdateLayout();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var activeCell = activeColumn.GetCellContent(table.Items[0]);
        activeCell.ShouldBeOfType<CheckBox>();
        var activeCheckBox = (CheckBox)activeCell;
        activeCheckBox.IsChecked.ShouldBe(true);
        activeCheckBox.GetBindingExpression(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty)!
            .HasError.ShouldBeFalse();
        Find<TextBox>(window, "ProductSearch").Text = "missing";
        table.Items.Count.ShouldBe(0);
        Find<TextBox>(window, "ProductSearch").Text = "තේ";
        table.Items.Count.ShouldBe(1);
        table.SelectedIndex = 0;
        Find<TextBox>(window, "Sku").Text.ShouldBe("TEA-01");
        Capture(window, "products-en.png");
    }

    private static void Enter(UIElement target, Window window) => target.RaiseEvent(new KeyEventArgs(
        Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, Environment.TickCount, Key.Enter)
        { RoutedEvent = Keyboard.KeyDownEvent });

    private static void AssertInClient(Window window, FrameworkElement element)
    {
        element.IsVisible.ShouldBeTrue(); element.ActualWidth.ShouldBeGreaterThan(0); element.ActualHeight.ShouldBeGreaterThan(0);
        var client = (FrameworkElement)window.Content;
        var bounds = element.TransformToAncestor(client).TransformBounds(new Rect(element.RenderSize));
        var context = $"Control={AutomationProperties.GetAutomationId(element)} ({element.GetType().Name}), "
            + $"language={Texts.Language}, window={window.Width}x{window.Height}, bounds={bounds}, client={client.RenderSize}";
        bounds.Left.ShouldBeGreaterThanOrEqualTo(-1, context); bounds.Top.ShouldBeGreaterThanOrEqualTo(-1, context);
        bounds.Right.ShouldBeLessThanOrEqualTo(client.ActualWidth + 1, context);
        bounds.Bottom.ShouldBeLessThanOrEqualTo(client.ActualHeight + 1, context);
    }

    private static async Task CaptureAuthenticationSizesAsync(Window window, MainViewModel vm, string screen)
    {
        var originalLanguage = Texts.Language;
        foreach (var language in new[] { "en", "si" })
        {
            await vm.ChangeLanguageAsync(language);
            foreach (var size in new[] { (800d, 600d), (1280d, 850d) })
            {
                window.Width = size.Item1; window.Height = size.Item2;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Descendants(window).OfType<ScrollViewer>().Single(view => view.Content is Border).ScrollToTop();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Capture(window, $"modern-{screen}-{language}-{size.Item1}x{size.Item2}.png");
                var pin = Find<PasswordBox>(window, "Pin");
                pin.BringIntoView(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                AssertInClient(window, pin);
                if (screen == "setup") Capture(window, $"modern-setup-pin-{language}-{size.Item1}x{size.Item2}.png");
            }
        }
        await vm.ChangeLanguageAsync(originalLanguage);
    }

    private static async Task ReviewBillingSizesAsync(Window window, MainViewModel vm)
    {
        var originalLanguage = Texts.Language;
        foreach (var language in new[] { "en", "si" })
        {
            await vm.ChangeLanguageAsync(language);
            foreach (var size in new[] { (800d, 600d), (1280d, 850d) })
            {
                window.Width = size.Item1; window.Height = size.Item2;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
                // Keep the actual layout available for diagnosis even if a bounds assertion fails.
                Capture(window, $"modern-billing-basket-{language}-{size.Item1}x{size.Item2}.png");
                foreach (var id in new[] { "ScanBarcode", "BillingSearch", "BillingBasket", "BillingTotal", "PaymentMethod", "CashTendered", "CompleteSale" })
                    AssertInClient(window, Find<FrameworkElement>(window, id));
                var cart = Find<DataGrid>(window, "BillingBasket");
                cart.Columns.Sum(c => c.ActualWidth).ShouldBeLessThanOrEqualTo(cart.ActualWidth + 1);
                var row = (DataGridRow)cart.ItemContainerGenerator.ContainerFromIndex(0);
                row.ShouldNotBeNull(); AssertInClient(window, row);
                var rowBounds = row.TransformToAncestor(cart).TransformBounds(new Rect(row.RenderSize));
                rowBounds.Bottom.ShouldBeLessThanOrEqualTo(cart.ActualHeight + 1,
                    $"First basket row must fit: language={language}, window={size.Item1}x{size.Item2}, "
                    + $"basket={cart.ActualWidth:0.##}x{cart.ActualHeight:0.##}, row={rowBounds}");
                var picker = Find<Expander>(window, "BillingProductPicker"); picker.IsExpanded = true;
                await FindVisibleAsync<DataGrid>(window, "BillingProducts");
                window.UpdateLayout();
                AssertInClient(window, Find<Button>(window, "CompleteSale"));
                AssertInClient(window, cart);
                Capture(window, $"modern-billing-picker-{language}-{size.Item1}x{size.Item2}.png");
                picker.IsExpanded = false;
            }
        }
        await vm.ChangeLanguageAsync(originalLanguage);
    }

    private static async Task ReviewScreenLayoutsAsync(Window window, MainViewModel vm)
    {
        foreach (var language in new[] { "en", "si" })
        {
            await vm.ChangeLanguageAsync(language);
            foreach (var size in new[] { (800d, 600d), (1280d, 850d) })
            {
                window.Width = size.Item1; window.Height = size.Item2;
                foreach (var section in new[] { "Billing", "Products", "Stock", "Returns", "Reports", "Settings" })
                {
                    Click(window, section); await UntilAsync(() => !vm.Busy && vm.Section == section);
                    foreach (var nav in new[] { "Billing", "Products", "Stock", "Returns", "Reports", "Settings" })
                        AssertInClient(window, Find<Button>(window, nav));
                    AssertInClient(window, Find<ComboBox>(window, "UiLanguage"));
                    AssertInClient(window, Find<TextBlock>(window, "BackupHealth"));
                    Capture(window, $"modern-{section.ToLowerInvariant()}-{language}-{size.Item1}x{size.Item2}.png");
                }
            }
        }
    }

    private static void Click(DependencyObject root, string id) =>
        Find<Button>(root, id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static T Find<T>(DependencyObject root, string id) where T : DependencyObject
    {
        // ContentControl replacements do not attach their visual children synchronously.
        // Realize the current content before inspecting the visual tree.
        if (root is UIElement element) { element.UpdateLayout(); }
        var controls = Descendants(root).OfType<T>().ToArray();
        var matches = controls.Where(c => AutomationProperties.GetAutomationId(c) == id).ToArray();
        if (matches.Length != 1)
        {
            var available = string.Join(", ", controls.Select(AutomationProperties.GetAutomationId)
                .Where(value => !string.IsNullOrEmpty(value)).Distinct().Order(StringComparer.Ordinal));
            throw new InvalidOperationException(
                $"Expected one {typeof(T).Name} with AutomationId '{id}', found {matches.Length}. Available IDs: [{available}].");
        }

        return matches[0];
    }

    private static async Task<T> FindVisibleAsync<T>(FrameworkElement root, string id) where T : FrameworkElement
    {
        T? match = null;
        // Material's Expander starts its visibility storyboard on a dispatcher/render tick.
        // UpdateLayout alone cannot realize content that is still collapsed by that storyboard.
        await UntilAsync(() =>
        {
            root.UpdateLayout();
            var matches = Descendants(root).OfType<T>()
                .Where(control => AutomationProperties.GetAutomationId(control) == id).ToArray();
            matches.Length.ShouldBeLessThanOrEqualTo(1, $"AutomationId '{id}' must remain unique.");
            match = matches.SingleOrDefault();
            if (match is not { IsLoaded: true, IsVisible: true, ActualWidth: > 0, ActualHeight: > 0 }) return false;
            // Visibility changes at the start of expansion; wait until the parent scale has
            // also revealed the control at its measured size before scrolling/focusing it.
            var bounds = match.TransformToAncestor(root).TransformBounds(new Rect(match.RenderSize));
            return bounds.Width >= match.ActualWidth - .01 && bounds.Height >= match.ActualHeight - .01;
        });
        return match!;
    }

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
        while (true)
        {
            // Let rendering, templates and Loaded handlers settle before checking readiness.
            // Checking only view-model state can otherwise return before the new view exists.
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            if (predicate()) { return; }
            if (DateTimeOffset.UtcNow >= limit) { throw new TimeoutException("WPF workflow did not reach its expected state."); }
            await Task.Delay(25);
        }
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
