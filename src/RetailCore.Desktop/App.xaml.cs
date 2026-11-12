using System.Windows;
using System.IO;
using System.Windows.Threading;
using RetailCore.Application.Foundation;
using RetailCore.Infrastructure.Sqlite.Recovery;
using Microsoft.Extensions.DependencyInjection;
using RetailCore.Infrastructure.Sqlite.Foundation;

namespace RetailCore.Desktop;

public partial class App : System.Windows.Application, IDisposable
{
    private ServiceProvider? _services;
    private Mutex? _instanceMutex;
    private bool _ownsMutex;
    private DispatcherTimer? _backupTimer;
    private bool _backingUp;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _instanceMutex = new Mutex(true, "Local\\RetailCore.SingleCheckout", out _ownsMutex);
        if (!_ownsMutex)
        {
            if(!e.Args.Any(a=>a is "--daily-backup" or "--prepare-upgrade"))MessageBox.Show(Texts.Get("AlreadyRunning"), Texts.Get("AppTitle"));
            Shutdown(e.Args.Contains("--prepare-upgrade",StringComparer.Ordinal)?1:0);
            return;
        }

        try
        {
            var collection = new ServiceCollection();
            collection.AddLocalFoundation(LocalRuntime.DefaultDatabasePath);
            _services = collection.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
            var engine=_services.GetRequiredService<SqliteRecovery>();
            if(e.Args.Contains("--prepare-upgrade",StringComparer.Ordinal))
            {await Task.Run(()=>engine.PrepareUpgradeAsync());Shutdown(0);return;}
            if(e.Args.Contains("--daily-backup",StringComparer.Ordinal))
            {
                var status=await Task.Run(()=>engine.DailyAsync());
                Shutdown(status.State is "BackupFailed" or "BackupSecondFailed"?1:0);return;
            }
            await _services.GetRequiredService<ILocalTransactions>().InitializeAsync();
            var vm = new MainViewModel(_services.GetRequiredService<RetailCore.Application.Foundation.FoundationService>(),
                _services.GetRequiredService<RetailCore.Application.Foundation.LocalSession>(),
                _services.GetRequiredService<RetailCore.Application.Catalogue.CatalogueService>(),
                _services.GetRequiredService<RetailCore.Application.Inventory.StockService>(),
                _services.GetRequiredService<RetailCore.Application.Checkout.BillingService>(),
                _services.GetRequiredService<RetailCore.Application.Receipts.ReceiptService>(), _services.GetRequiredService<RetailCore.Application.Returns.ReturnService>(),
                _services.GetRequiredService<RetailCore.Application.Reporting.CashService>(), _services.GetRequiredService<RetailCore.Application.Reporting.ReportService>(), _services.GetRequiredService<RetailCore.Application.Recovery.RecoveryService>());
            MainWindow = new MainWindow(vm);
            MainWindow.Show();
            async Task Daily()
            {
                if(_backingUp)return;_backingUp=true;
                try {var result=await Task.Run(()=>engine.DailyAsync());vm.SetBackupHealth(result.State);}
                catch(Exception){vm.SetBackupHealth("BackupFailed");}
                finally{_backingUp=false;}
            }
            _backupTimer=new DispatcherTimer{Interval=TimeSpan.FromMinutes(15)};
            _backupTimer.Tick+=async(_,_)=>await Daily();_backupTimer.Start();await Daily();
        }
        catch (FoundationException ex)
        {
            LogStartupFailure(ex.Key);
            if(!e.Args.Any(a=>a is "--daily-backup" or "--prepare-upgrade"))MessageBox.Show(Texts.Get(ex.Key), Texts.Get("AppTitle"));Shutdown(1);
        }
        catch (Exception ex)
        {
            LogStartupFailure(ex.GetType().Name);
            if(!e.Args.Any(a=>a is "--daily-backup" or "--prepare-upgrade"))MessageBox.Show(Texts.Get("StorageError"), Texts.Get("AppTitle"));
            Shutdown(1);
        }
    }

    private static void LogStartupFailure(string category)
    {
        try
        {
            var path=LocalRuntime.DefaultDatabasePath+".recovery";Directory.CreateDirectory(path);
            var log=Path.Combine(path,"startup.log");
            if(File.Exists(log) && new FileInfo(log).Length>1024*1024)File.Move(log,log+".previous",true);
            File.AppendAllText(log,$"{DateTimeOffset.UtcNow:O} {typeof(App).Assembly.GetName().Version} {category}{Environment.NewLine}");
        }
        catch(IOException) { }
        catch(UnauthorizedAccessException) { }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Dispose();
        base.OnExit(e);
    }

    public void Dispose()
    {
        _backupTimer?.Stop();
        _services?.Dispose();
        _services = null;
        if (_ownsMutex) { _instanceMutex?.ReleaseMutex(); _ownsMutex = false; }
        _instanceMutex?.Dispose();
        _instanceMutex = null;
        GC.SuppressFinalize(this);
    }
}
