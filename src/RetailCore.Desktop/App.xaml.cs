using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using RetailCore.Infrastructure.Sqlite.Foundation;

namespace RetailCore.Desktop;

public partial class App : System.Windows.Application, IDisposable
{
    private ServiceProvider? _services;
    private Mutex? _instanceMutex;
    private bool _ownsMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _instanceMutex = new Mutex(true, "Local\\RetailCore.SingleCheckout", out _ownsMutex);
        if (!_ownsMutex)
        {
            MessageBox.Show(Texts.Get("AlreadyRunning"), Texts.Get("AppTitle"));
            Shutdown();
            return;
        }

        try
        {
            var collection = new ServiceCollection();
            collection.AddLocalFoundation(LocalRuntime.DefaultDatabasePath);
            _services = collection.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
            var vm = new MainViewModel(_services.GetRequiredService<RetailCore.Application.Foundation.FoundationService>(),
                _services.GetRequiredService<RetailCore.Application.Foundation.LocalSession>(),
                _services.GetRequiredService<RetailCore.Application.Catalogue.CatalogueService>(),
                _services.GetRequiredService<RetailCore.Application.Inventory.StockService>(),
                _services.GetRequiredService<RetailCore.Application.Checkout.BillingService>(),
                _services.GetRequiredService<RetailCore.Application.Receipts.ReceiptService>(), _services.GetRequiredService<RetailCore.Application.Returns.ReturnService>(),
                _services.GetRequiredService<RetailCore.Application.Reporting.CashService>(), _services.GetRequiredService<RetailCore.Application.Reporting.ReportService>());
            MainWindow = new MainWindow(vm);
            MainWindow.Show();
        }
        catch (Exception)
        {
            MessageBox.Show(Texts.Get("StorageError"), Texts.Get("AppTitle"));
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Dispose();
        base.OnExit(e);
    }

    public void Dispose()
    {
        _services?.Dispose();
        _services = null;
        if (_ownsMutex) { _instanceMutex?.ReleaseMutex(); _ownsMutex = false; }
        _instanceMutex?.Dispose();
        _instanceMutex = null;
        GC.SuppressFinalize(this);
    }
}
