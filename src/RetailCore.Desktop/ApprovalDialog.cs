using System.Windows;
using System.Windows.Controls;
using RetailCore.Application.Foundation;

namespace RetailCore.Desktop;

public sealed class ApprovalDialog : Window
{
    public ApprovalTicket? Ticket { get; private set; }

    public ApprovalDialog(FoundationService service, IReadOnlyList<UserSummary> owners, ShopProfile profile)
        : this(service, owners, FoundationService.SettingsApproval(profile), Describe(profile)) { }

    private static string Describe(ShopProfile profile) => $"{Texts.Get("ShopName")}: {profile.Name}\n{Texts.Get("LegalName")}: {profile.LegalName}\n"
                + $"{Texts.Get("Address")}: {profile.Address}\n{Texts.Get("Telephone")}: {profile.Telephone}\n"
                + $"{Texts.Get("TimeZone")}: {profile.TimeZoneId}\n{Texts.Get("Currency")}: {profile.Currency}\n"
                + $"{Texts.Get("ReceiptFooter")}: {profile.ReceiptFooter}\n{Texts.Get("PrinterName")}: {profile.PrinterName}\n"
                + $"{Texts.Get("PaperWidth")}: {profile.PaperWidthMillimeters}\n{Texts.Get("CashIncrement")}: {profile.CashIncrementMinor}";

    public ApprovalDialog(FoundationService service, IReadOnlyList<UserSummary> owners, ApprovalRequest request, string review)
    {
        Title = Texts.Get("OwnerApproval");
        Width = 560;
        Height = 650;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontSize = 16;
        FontFamily = new System.Windows.Media.FontFamily("Nirmala UI, Iskoola Pota, Segoe UI");
        var panel = MainWindow.Form("OwnerApproval");
        panel.Margin = new Thickness(24);
        panel.Children.Add(new TextBlock { Text = Texts.Get("ApprovalHelp"), TextWrapping = TextWrapping.Wrap });
        // Show the values the owner is actually authorizing, not an opaque token or JSON.
        panel.Children.Add(new TextBlock
        {
            Text = review,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 16, 0, 12),
        });
        MainWindow.Label(panel, "SelectUser");
        var owner = new ComboBox { ItemsSource = owners, DisplayMemberPath = nameof(UserSummary.Name), SelectedIndex = 0 };
        panel.Children.Add(owner);
        var pin = MainWindow.Password(panel, "Pin");
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.DarkRed };
        panel.Children.Add(error);
        var approve = new Button { Content = Texts.Get("Approve"), IsDefault = true };
        System.Windows.Automation.AutomationProperties.SetAutomationId(approve, "Approve");
        var cancel = new Button { Content = Texts.Get("Cancel"), IsCancel = true };
        approve.Click += async (_, _) =>
        {
            approve.IsEnabled = false;
            cancel.IsEnabled = false;
            try
            {
                if (owner.SelectedItem is not UserSummary selected) { throw new FoundationException("OwnerRequired"); }
                Ticket = await service.ApproveAsync(selected.Id, pin.Password, request);
                if (IsVisible) { DialogResult = true; }
            }
            catch (FoundationException ex) { error.Text = Texts.Get(ex.Key); }
            catch (Exception) { error.Text = Texts.Get("StorageError"); }
            finally { pin.Clear(); approve.IsEnabled = true; cancel.IsEnabled = true; }
        };
        panel.Children.Add(approve);
        panel.Children.Add(cancel);
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Loaded += (_, _) => pin.Focus();
        Closed += (_, _) => pin.Clear();
    }
}
