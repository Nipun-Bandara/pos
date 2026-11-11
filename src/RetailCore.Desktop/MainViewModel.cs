using System.ComponentModel;
using System.Runtime.CompilerServices;
using RetailCore.Application.Foundation;
using RetailCore.Domain.Common;
using RetailCore.Domain.Security;

namespace RetailCore.Desktop;

public sealed class MainViewModel(FoundationService service, LocalSession session, RetailCore.Application.Catalogue.CatalogueService catalogue, RetailCore.Application.Inventory.StockService stock, RetailCore.Application.Checkout.BillingService billing, RetailCore.Application.Receipts.ReceiptService receipts, RetailCore.Application.Returns.ReturnService returns, RetailCore.Application.Reporting.CashService cash, RetailCore.Application.Reporting.ReportService reports) : INotifyPropertyChanged
{
    public RetailCore.Application.Reporting.CashService Cash { get; } = cash;
    public RetailCore.Application.Reporting.ReportService Reports { get; } = reports;
    public RetailCore.Application.Returns.ReturnService ReturnsService { get; } = returns;
    public RetailCore.Application.Returns.ReturnBill? ReturnBill { get; private set; }
    public async Task FindReturnBillAsync(string number)
    {
        ReturnBill=await ReturnsService.FindAsync(number);Approvers=await Service.ApproversAsync();Notify(nameof(Screen));
    }
    public async Task RefreshReturnBillAsync()
    {
        if(ReturnBill is not null)ReturnBill=await ReturnsService.LoadAsync(ReturnBill.Id);
        Notify(nameof(Screen));
    }
    public RetailCore.Application.Receipts.ReceiptService Receipts { get; } = receipts;
    public IReadOnlyList<RetailCore.Application.Receipts.BillSummary> Bills { get; private set; } = [];
    public RetailCore.Application.Receipts.BillDetail? SelectedBill { get; private set; }
    public RetailCore.Application.Receipts.ReceiptSettings? ReceiptSettings { get; private set; }
    public async Task LoadBillsAsync(RetailCore.Application.Receipts.BillFilter filter)
    {
        Bills = await Receipts.HistoryAsync(filter); SelectedBill = null; Notify(nameof(Screen));
    }
    public async Task OpenBillAsync(Guid? id = null, string? number = null)
    {
        SelectedBill = id is { } value ? await Receipts.DetailAsync(value) : await Receipts.FindAsync(number!);
        ReceiptSettings = await Receipts.SettingsAsync(); Section = "Reports"; Notify(nameof(Screen));
    }
    public async Task PrintBillAsync(Guid id, RetailCore.Application.Receipts.IReceiptPrinter printer)
    {
        var result = await Receipts.PrintAsync(id, Guid.NewGuid(), printer);
        SelectedBill = await Receipts.DetailAsync(id); ReceiptSettings = await Receipts.SettingsAsync();
        Notify(nameof(Screen)); ShowMessage(result.Status);
    }
    public RetailCore.Application.Checkout.BillingService Billing { get; } = billing;
    public RetailCore.Application.Checkout.BillingState? BillingState { get; private set; }
    public IReadOnlyList<UserSummary> Approvers { get; private set; } = [];
    public void SetBillingState(RetailCore.Application.Checkout.BillingState state)
    {
        BillingState = state;
        if (Section == "Billing") Notify(nameof(Screen));
    }
    public void ShowMessage(string key) { Message = Texts.Get(key); Notify(); }
    public RetailCore.Application.Inventory.StockService Stock { get; } = stock;
    public RetailCore.Application.Inventory.StockData? StockData { get; private set; }
    public event PropertyChangedEventHandler? PropertyChanged;
    public RetailCore.Application.Catalogue.CatalogueService Catalogue { get; } = catalogue;
    public RetailCore.Application.Catalogue.CatalogueData? CatalogueData { get; private set; }
    public FoundationService Service { get; } = service;
    public LocalSession Session { get; } = session;
    public StartupInfo? Startup { get; private set; }
    public UserSummary? Current { get; private set; }
    public ShopProfile? Profile { get; private set; }
    public IReadOnlyList<UserSummary> Users { get; private set; } = [];
    public string Screen { get; private set; } = "Loading";
    public string Section { get; private set; } = "Billing";
    public bool Busy { get; private set; }
    public bool Unlocking { get; private set; }
    public bool IsOwner => Current?.Role == Role.Owner && !Session.IsLocked;
    public string Message { get; private set; } = "";

    public async Task RunAsync(Func<Task> action)
    {
        if (Busy) { return; }
        Busy = true;
        Message = "";
        Notify();
        try { await action(); }
        catch (FoundationException ex) { Message = Texts.Get(ex.Key); }
        catch (Exception) { Message = Texts.Get("StorageError"); }
        finally { Busy = false; Notify(); }
    }

    public async Task StartAsync()
    {
        Startup = await Service.StartAsync();
        Screen = Startup.NeedsSetup ? "Setup" : "Login";
        Notify(nameof(Screen));
    }

    public async Task LoginAsync(UserId id, string pin)
    {
        Current = await Service.SignInAsync(id, pin, Unlocking);
        Texts.Use(Current.Language);
        Unlocking = false;
        BillingState = await Billing.LoadAsync();
        Approvers = await Service.ApproversAsync();
        Section = "Billing";
        Screen = "Shell";
        Notify(nameof(Screen));
    }

    public async Task NavigateAsync(string section)
    {
        Current = await Service.CurrentAsync();
        if (section is "Settings" or "Stock" && IsOwner)
        {
            Profile = await Service.ProfileAsync();
            Users = await Service.UsersAsync();
        }

        if (section == "Returns") Approvers = await Service.ApproversAsync();
        if (section == "Billing") { BillingState = await Billing.LoadAsync(); Approvers = await Service.ApproversAsync(); }
        if (section == "Reports")
        {
            ReceiptSettings = await Receipts.SettingsAsync();
            Bills = await Receipts.HistoryAsync(new(ReceiptSettings.Today, ReceiptSettings.Today)); SelectedBill = null;
        }
        if (section == "Products") CatalogueData = await Catalogue.LoadAsync();
        if (section == "Stock") StockData = await Stock.LoadAsync();
        Section = section;
        Notify(nameof(Screen));
    }

    public void ShowSaved()
    {
        Message = Texts.Get("Saved");
        Notify();
    }

    public async Task ChangeLanguageAsync(string language)
    {
        if (Current is not null && !Session.IsLocked)
        {
            await Service.SetLanguageAsync(language);
            Current = await Service.CurrentAsync();
        }

        Texts.Use(language);
        Notify(nameof(Screen));
    }

    public void Lock()
    {
        if (Current is null || Session.IsLocked) { return; }
        Service.Lock();
        BillingState = null;
        Bills = []; SelectedBill = null; ReceiptSettings = null; ReturnBill = null;
        Approvers = [];
        StockData = null;
        CatalogueData = null;
        Profile = null;
        Users = [];
        Unlocking = true;
        Screen = "Login";
        Message = "";
        Notify(nameof(Screen));
    }

    public async Task SignOutAsync()
    {
        Service.SignOut();
        Current = null;
        BillingState = null;
        Bills = []; SelectedBill = null; ReceiptSettings = null; ReturnBill = null;
        Approvers = [];
        StockData = null;
        CatalogueData = null;
        Profile = null;
        Users = [];
        Unlocking = false;
        await StartAsync();
    }

    private void Notify([CallerMemberName] string? property = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
