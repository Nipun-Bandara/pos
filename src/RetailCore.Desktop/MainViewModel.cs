using System.ComponentModel;
using System.Runtime.CompilerServices;
using RetailCore.Application.Foundation;
using RetailCore.Domain.Common;
using RetailCore.Domain.Security;

namespace RetailCore.Desktop;

public sealed class MainViewModel(FoundationService service, LocalSession session, RetailCore.Application.Catalogue.CatalogueService catalogue, RetailCore.Application.Inventory.StockService stock, RetailCore.Application.Checkout.BillingService billing) : INotifyPropertyChanged
{
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

        if (section == "Billing") { BillingState = await Billing.LoadAsync(); Approvers = await Service.ApproversAsync(); }
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
