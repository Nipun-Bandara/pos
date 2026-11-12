using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using RetailCore.Application.Foundation;
using RetailCore.Application.Recovery;
namespace RetailCore.Desktop;

public sealed partial class MainWindow
{
    private StackPanel RecoveryView()
    {
        var panel = new StackPanel(); Label(panel, "RecoveryTitle"); Label(panel, "RecoveryHelp");
        var statusText = new TextBlock { TextWrapping = TextWrapping.Wrap }; panel.Children.Add(statusText);
        var primary = Field(panel, "BackupPrimary"); var second = Field(panel, "BackupSecond"); var keep = Field(panel, "BackupRetention", "30");
        foreach (var (key, field) in new[] { ("ChooseBackupPrimary", primary), ("ChooseBackupSecond", second) })
            panel.Children.Add(Button(key, () => { var dialog = new OpenFolderDialog { Title = Texts.Get(key) }; if (dialog.ShowDialog(this) == true) field.Text = dialog.FolderName; return Task.CompletedTask; }));
        void Show(BackupStatus status)
        {
            _vm.SetBackupHealth(status.State); primary.Text = status.Settings.PrimaryDirectory; second.Text = status.Settings.SecondDirectory ?? ""; keep.Text = status.Settings.RetainCount.ToString(CultureInfo.InvariantCulture);
            statusText.Text = $"{Texts.Get(status.State)}\n{Texts.Get("BackupLastPrimary")}: {status.LastPrimarySuccess?.ToLocalTime():g}\n{Texts.Get("BackupLastSecond")}: {status.LastSecondSuccess?.ToLocalTime():g}\n{status.LastFile}\n{(status.LastMaintenanceState is { } maintenance ? Texts.Get(maintenance) : string.Empty)}\n{status.LastSafetyFile}";
        }
        panel.Children.Add(Button("RefreshBackup", async () => Show(await _vm.Recovery.StatusAsync())));
        panel.Children.Add(Button("SaveBackupSettings", async () =>
        {
            if (!int.TryParse(keep.Text, out var retention)) throw new FoundationException("InvalidBackupSettings");
            Show(await _vm.Recovery.ConfigureAsync(new(primary.Text, second.Text, retention)));
        }));
        panel.Children.Add(Button("BackupNow", async () => Show(await Task.Run(() => _vm.Recovery.BackupAsync()))));
        Label(panel, "RestoreWarning"); var file = Field(panel, "RestoreFile"); var reason = Field(panel, "RestoreReason");
        panel.Children.Add(Button("ChooseRestoreFile", () => { var dialog = new OpenFileDialog { Filter = Texts.Get("BackupFileFilter"), CheckFileExists = true }; if (dialog.ShowDialog(this) == true) file.Text = dialog.FileName; return Task.CompletedTask; }));
        RestoreReview? selected = null; var reviewText = new TextBlock { TextWrapping = TextWrapping.Wrap }; panel.Children.Add(reviewText);
        panel.Children.Add(Button("ReviewRestore", async () =>
        {
            selected = await Task.Run(() => _vm.Recovery.ReviewAsync(file.Text));
            reviewText.Text = $"{selected.Shop}\n{Texts.Get("BackupArchiveDate")}: {selected.ArchiveWrittenAt.ToLocalTime():g}\n{Texts.Get("RestoreBills")}: {selected.Bills}\n{Texts.Get("RestoreMovements")}: {selected.Movements}\n{Texts.Get("RestoreShifts")}: {selected.Shifts}\n{Texts.Get("RestoreChecksum")}: {selected.Sha256}";
        }));
        panel.Children.Add(Button("RestoreBackup", async () =>
        {
            if (selected is null || file.Text != selected.Path) throw new FoundationException("ReviewRestoreFirst");
            var request = new RestoreRequest(Guid.NewGuid(), selected.Path, selected.Sha256, reason.Text);
            var owners = await _vm.Service.ApproversAsync();
            var dialog = new ApprovalDialog(_vm.Service, owners, RecoveryService.RestoreApproval(request), Texts.Get("RestoreWarning") + "\n" + reviewText.Text + "\n" + reason.Text) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            try { await Task.Run(() => _vm.Recovery.RestoreAsync(request, dialog.Ticket)); }
            finally { await _vm.SignOutAsync(); }
            _vm.ShowMessage("RestoreSucceeded");
        }));
        panel.Loaded += async (_, _) => await _vm.RunAsync(async () => Show(await _vm.Recovery.StatusAsync()));
        return panel;
    }
}
