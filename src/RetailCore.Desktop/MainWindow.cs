using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using RetailCore.Application.Foundation;
using RetailCore.Domain.Security;

namespace RetailCore.Desktop;

/// <summary>WPF view composition; all state transitions and business calls go through the view model.</summary>
public sealed partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly DispatcherTimer _idleTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    private DateTimeOffset _lastInput = DateTimeOffset.UtcNow;

    public MainWindow(MainViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.Screen)) { Render(); }
            _backupHealth.Text = Texts.Get("BackupHealth")+": "+Texts.Get(_vm.BackupHealth);
            _status.Text = _vm.Busy ? Texts.Get("Loading") : _vm.Message;
            _body.IsEnabled = !_vm.Busy || (_vm.Screen == "Shell" && _vm.Section == "Billing");
            if (_billingActions is not null) _billingActions.IsEnabled = !_vm.Busy;
            _header.IsEnabled = !_vm.Busy;
            _nav.IsEnabled = !_vm.Busy;
        };
        Loaded += async (_, _) => { Render(); await _vm.RunAsync(_vm.StartAsync); };
        System.Windows.Input.InputManager.Current.PreProcessInput += RecordInput;
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized) { LockNow(); } };
        _idleTimer.Tick += (_, _) =>
        {
            if (DateTimeOffset.UtcNow - _lastInput >= TimeSpan.FromMinutes(5)) { LockNow(); }
        };
        _idleTimer.Start();
        PreviewKeyDown += HandleBillingShortcut;
        Closed += (_, _) =>
        {
            _idleTimer.Stop();
            System.Windows.Input.InputManager.Current.PreProcessInput -= RecordInput;
            _vm.Service.SignOut();
        };
    }

    private void RecordInput(object sender, System.Windows.Input.PreProcessInputEventArgs e)
    {
        if (e.StagingItem.Input is System.Windows.Input.KeyboardEventArgs or System.Windows.Input.MouseEventArgs)
        {
            _lastInput = DateTimeOffset.UtcNow;
        }
    }

    private void LockNow()
    {
        if (_vm.Current is null || _vm.Session.IsLocked) { return; }
        _vm.Lock();
        foreach (var dialog in OwnedWindows.Cast<Window>().ToArray()) { dialog.Close(); }
    }

    private void Render()
    {
        if (_billingUiSession != _vm.Session.Version)
        {
            _billingUiSession = _vm.Session.Version;
            ResetReturnEditor();
            _billingTenderText = "0"; _billingSearch = ""; _billingCategory = null; _billingSelectedLine = null;
        }
        Title = Texts.Get("AppTitle");
        Language = System.Windows.Markup.XmlLanguage.GetLanguage(CultureInfo.CurrentUICulture.IetfLanguageTag);
        _appTitle.Text = Texts.Get("AppTitle");
        _currentUser.Text = _vm.Current is not null && !_vm.Session.IsLocked
            ? _vm.Current.Name + " · " + Texts.Get(_vm.IsOwner ? "Owner" : "Cashier") : string.Empty;
        var languages = LanguagePicker(Texts.Language);
        languages.SelectionChanged += async (_, _) =>
        {
            var code = languages.SelectedIndex == 1 ? "si" : "en";
            if (code != Texts.Language) await _vm.RunAsync(() => _vm.ChangeLanguageAsync(code));
        };
        AutomationProperties.SetName(languages, Texts.Get("Language"));
        AutomationProperties.SetAutomationId(languages, "UiLanguage");
        _languageSlot.Content = languages;
        var signedIn = _vm.Current is not null && !_vm.Session.IsLocked;
        _lockButton.Visibility = _signOutButton.Visibility = signedIn ? Visibility.Visible : Visibility.Collapsed;
        _lockButton.Content = Texts.Get("HeaderLock"); _signOutButton.Content = Texts.Get("HeaderSignOut");
        _lockButton.ToolTip = Texts.Get("Lock"); _signOutButton.ToolTip = Texts.Get("SignOut");
        AutomationProperties.SetName(_lockButton, Texts.Get("Lock"));
        AutomationProperties.SetName(_signOutButton, Texts.Get("SignOut"));
        _nav.Visibility = _vm.Screen == "Shell" ? Visibility.Visible : Visibility.Collapsed;
        _sectionTitle.Visibility = _vm.Screen == "Shell" && _vm.Section != "Billing" ? Visibility.Visible : Visibility.Collapsed;
        _sectionTitle.Text = Texts.Get(_vm.Section);
        foreach (var button in _navigation.Children.OfType<Button>())
        {
            var section = (string)button.Tag;
            button.Content = Texts.Get(section);
            AutomationProperties.SetName(button, Texts.Get(section));
            AutomationProperties.SetItemStatus(button, _vm.Section == section ? Texts.Get("CurrentSection") : string.Empty);
            button.Style = (Style)FindResource(_vm.Section == section ? "PosSelectedNavButton" : "PosNavButton");
        }
        _body.Content = _vm.Screen switch
        {
            "Setup" => SetupView(),
            "Login" => LoginView(),
            "Shell" => ShellView(),
            _ => Button("Retry", _vm.StartAsync),
        };
    }

    private ScrollViewer SetupView()
    {
        var panel = Form("Welcome");
        panel.Children.Add(new TextBlock { Text = Texts.Get("SetupHelp"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 20) });
        var name = Field(panel, "ShopName");
        var legal = Field(panel, "LegalName");
        var address = Field(panel, "Address");
        var phone = Field(panel, "Telephone");
        Label(panel, "Currency");
        var currency = new ComboBox { ItemsSource = RetailCore.Domain.Monetary.Currency.KnownCodes, SelectedItem = "LKR" };
        panel.Children.Add(currency);
        var zone = Field(panel, "TimeZone", "Asia/Colombo");
        var owner = Field(panel, "OwnerName");
        var pin = Password(panel, "Pin");
        var confirmation = Password(panel, "ConfirmPin");
        panel.Children.Add(Button("CreateShop", async () =>
        {
            try
            {
                if (pin.Password != confirmation.Password) { throw new FoundationException("PinMismatch"); }
                var profile = new ShopProfile(name.Text, legal.Text, (string)currency.SelectedItem, zone.Text,
                    address.Text, phone.Text, null, null, 80, 100);
                await _vm.Service.SetupAsync(profile, owner.Text, pin.Password, Texts.Language);
                await _vm.StartAsync();
            }
            finally { pin.Clear(); confirmation.Clear(); }
        }));
        return AuthPage(panel);
    }

    private ScrollViewer LoginView()
    {
        var panel = Form(_vm.Unlocking ? "Locked" : "SignIn");
        Label(panel, "SelectUser");
        var users = new ComboBox
        {
            ItemsSource = _vm.Startup?.Users,
            DisplayMemberPath = nameof(UserSummary.Name),
            SelectedItem = _vm.Unlocking ? _vm.Startup?.Users.FirstOrDefault(u => u.Id == _vm.Session.UserId) : (_vm.Startup?.Users is { Count: > 0 } choices ? choices[0] : null),
            IsEnabled = !_vm.Unlocking,
        };
        AutomationProperties.SetAutomationId(users, "LoginUser");
        panel.Children.Add(users);
        var pin = Password(panel, "Pin");
        async Task Login()
        {
            try
            {
                if (users.SelectedItem is not UserSummary user) { throw new FoundationException("InvalidCredentials"); }
                await _vm.LoginAsync(user.Id, pin.Password);
                _lastInput = DateTimeOffset.UtcNow;
            }
            finally { pin.Clear(); }
        }

        panel.Children.Add(Button(_vm.Unlocking ? "Unlock" : "SignIn", Login));
        pin.KeyDown += async (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter) { e.Handled = true; await _vm.RunAsync(Login); }
        };
        if (_vm.Unlocking) { panel.Children.Add(Button("SignOut", _vm.SignOutAsync)); }
        panel.Loaded += (_, _) => pin.Focus();
        return AuthPage(panel);
    }

    private async void NavigateClicked(object sender, RoutedEventArgs e) =>
        await _vm.RunAsync(() => _vm.NavigateAsync((string)((Button)sender).Tag));
    private void LockClicked(object sender, RoutedEventArgs e) => LockNow();
    private async void SignOutClicked(object sender, RoutedEventArgs e) => await _vm.RunAsync(_vm.SignOutAsync);

    private FrameworkElement ShellView() => _vm.Section switch
    {
        "Billing" => BillingView(),
        "Returns" => ReturnsView(),
        "Reports" => TradingView(),
        "Settings" when _vm.IsOwner => SettingsView(),
        "Stock" => StockView(),
        "Products" => ProductsView(),
        _ => new Border { Child = new TextBlock { Text = Texts.Get("OwnerRequired"), TextWrapping = TextWrapping.Wrap } },
    };

    private ScrollViewer SettingsView()
    {
        var p = _vm.Profile!;
        var panel = Form("SettingsTitle");
        panel.Children.Add(RecoveryView());
        var name = Field(panel, "ShopName", p.Name);
        var legal = Field(panel, "LegalName", p.LegalName);
        var currency = Field(panel, "Currency", p.Currency);
        currency.IsReadOnly = true;
        var zone = Field(panel, "TimeZone", p.TimeZoneId);
        var address = Field(panel, "Address", p.Address);
        var phone = Field(panel, "Telephone", p.Telephone);
        var footer = Field(panel, "ReceiptFooter", p.ReceiptFooter);
        var printer = Field(panel, "PrinterName", p.PrinterName);
        var width = Field(panel, "PaperWidth", p.PaperWidthMillimeters.ToString(CultureInfo.CurrentCulture));
        var increment = Field(panel, "CashIncrement", p.CashIncrementMinor.ToString(CultureInfo.CurrentCulture));
        panel.Children.Add(Button("SaveSettings", async () =>
        {
            if (!int.TryParse(width.Text, out var mm) || !long.TryParse(increment.Text, out var minor))
            {
                throw new FoundationException("InvalidProfile");
            }

            var changed = new ShopProfile(name.Text, legal.Text, p.Currency, zone.Text, address.Text, phone.Text,
                footer.Text, printer.Text, mm, minor);
            var dialog = new ApprovalDialog(_vm.Service, _vm.Users.Where(u => u.IsActive && u.Role == Role.Owner).ToArray(), changed)
            { Owner = this };
            if (dialog.ShowDialog() == true && dialog.Ticket is { } ticket)
            {
                await _vm.Service.SaveProfileAsync(changed, ticket);
                await _vm.NavigateAsync("Settings");
                _vm.ShowSaved();
            }
        }));
        panel.Children.Add(new Separator { Margin = new Thickness(0, 24, 0, 16) });
        panel.Children.Add(new TextBlock { Text = Texts.Get("UserManagement"), FontSize = 24 });
        var newName = Field(panel, "UserName");
        var newPin = Password(panel, "Pin");
        var confirm = Password(panel, "ConfirmPin");
        Label(panel, "Role");
        var newRole = RolePicker(Role.Cashier);
        panel.Children.Add(newRole);
        Label(panel, "Language");
        var newLanguage = LanguagePicker(Texts.Language);
        panel.Children.Add(newLanguage);
        panel.Children.Add(Button("CreateUser", async () =>
        {
            try
            {
                if (newPin.Password != confirm.Password) { throw new FoundationException("PinMismatch"); }
                await _vm.Service.CreateUserAsync(newName.Text, newPin.Password,
                    newRole.SelectedIndex == 1 ? Role.Owner : Role.Cashier, newLanguage.SelectedIndex == 1 ? "si" : "en");
                await _vm.NavigateAsync("Settings");
            }
            finally { newPin.Clear(); confirm.Clear(); }
        }));
        panel.Children.Add(new Separator { Margin = new Thickness(0, 24, 0, 16) });
        Label(panel, "EditUser");
        var users = new ComboBox { ItemsSource = _vm.Users, DisplayMemberPath = nameof(UserSummary.Name) };
        panel.Children.Add(users);
        Label(panel, "Role");
        var role = RolePicker(Role.Cashier);
        panel.Children.Add(role);
        var active = new CheckBox { Content = Texts.Get("Active"), Margin = new Thickness(0, 12, 0, 12) };
        panel.Children.Add(active);
        var resetPin = Password(panel, "NewPin");
        var clear = new CheckBox { Content = Texts.Get("ClearLockout"), Margin = new Thickness(0, 12, 0, 12) };
        panel.Children.Add(clear);
        users.SelectionChanged += (_, _) =>
        {
            if (users.SelectedItem is not UserSummary user) { return; }
            active.IsChecked = user.IsActive;
            role.SelectedIndex = user.Role switch { Role.Cashier => 0, Role.Owner => 1, _ => -1 };
            resetPin.Clear();
            clear.IsChecked = false;
        };
        users.SelectedIndex = 0;
        panel.Children.Add(Button("UpdateUser", async () =>
        {
            try
            {
                if (users.SelectedItem is not UserSummary user || role.SelectedIndex < 0) { throw new FoundationException("InvalidUser"); }
                await _vm.Service.UpdateUserAsync(user.Id, role.SelectedIndex == 1 ? Role.Owner : Role.Cashier,
                    active.IsChecked == true, resetPin.Password, clear.IsChecked == true);
                if (_vm.Session.IsLocked) { await _vm.SignOutAsync(); }
                else { await _vm.NavigateAsync("Settings"); }
            }
            finally { resetPin.Clear(); }
        }));
        ArrangeFields(panel);
        return Scroll(panel);
    }

    private Button Button(string key, Func<Task> action)
    {
        var button = new Button { Content = Texts.Get(key), HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetAutomationId(button, key);
        AutomationProperties.SetName(button, Texts.Get(key));
        if (PrimaryActions.Contains(key)) button.SetResourceReference(StyleProperty, "PosPrimaryButton");
        button.Click += async (_, _) => await _vm.RunAsync(action);
        return button;
    }

    internal static StackPanel Form(string heading)
    {
        var panel = new StackPanel { MaxWidth = 680, HorizontalAlignment = HorizontalAlignment.Stretch };
        panel.Children.Add(new TextBlock { Text = Texts.Get(heading), Style = (Style)System.Windows.Application.Current.FindResource("PosPageTitle") });
        return panel;
    }

    internal static void Label(Panel panel, string key) => panel.Children.Add(new TextBlock
    {
        Text = Texts.Get(key), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 2),
    });

    internal static TextBox Field(Panel panel, string key, string? value = null)
    {
        Label(panel, key);
        var field = new TextBox { Text = value ?? "" };
        AutomationProperties.SetAutomationId(field, key);
        AutomationProperties.SetName(field, Texts.Get(key));
        panel.Children.Add(field);
        return field;
    }

    internal static PasswordBox Password(Panel panel, string key)
    {
        Label(panel, key);
        var field = new PasswordBox { MaxLength = 12 };
        AutomationProperties.SetAutomationId(field, key);
        AutomationProperties.SetName(field, Texts.Get(key));
        panel.Children.Add(field);
        return field;
    }

    private static ScrollViewer Scroll(UIElement content) => new()
    {
        Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
    };

    private static ComboBox LanguagePicker(string language) => new()
    {
        ItemsSource = new[] { "English", "සිංහල" }, SelectedIndex = language == "si" ? 1 : 0,
    };

    private static ComboBox RolePicker(Role role) => new()
    {
        ItemsSource = new[] { Texts.Get("Cashier"), Texts.Get("Owner") }, SelectedIndex = role == Role.Owner ? 1 : 0,
    };
}
