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
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(90));
    }

    private static async Task ExerciseAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), "retailcore-wpf-" + Guid.NewGuid().ToString("N"));
        var application = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        application.InitializeComponent();
        Texts.Use("en");
        var services = new ServiceCollection().AddLocalFoundation(Path.Combine(path, "till.db"));
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var vm = new MainViewModel(provider.GetRequiredService<FoundationService>(), provider.GetRequiredService<LocalSession>());
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
            vm = new MainViewModel(provider.GetRequiredService<FoundationService>(), provider.GetRequiredService<LocalSession>());
            window = new MainWindow(vm);
            window.Show();
            await UntilAsync(() => vm.Screen == "Login" && !vm.Busy);
            var users = Find<ComboBox>(window, "LoginUser");
            users.SelectedItem = vm.Startup!.Users.Single(u => u.Id == ownerId);
            Find<PasswordBox>(window, "Pin").Password = "638194";
            Click(window, "SignIn");
            await UntilAsync(() => vm.Screen == "Shell" && !vm.Busy);
            Texts.Language.ShouldBe("si");
            vm.Current!.Language.ShouldBe("si");
            (await vm.Service.ProfileAsync()).Telephone.ShouldBe("0771234567");
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
        }
        finally
        {
            window.Close();
            application.Shutdown();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(path, true);
        }
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
