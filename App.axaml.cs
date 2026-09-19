using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using NullWave.Views;
using NullWave.Helpers.Logging;
using NullWave.Services;
using Serilog;
using System.Net.Http;
using System.Net.Sockets;

namespace NullWave;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        var prefs = new PreferencesService().Current;

        // Initialize localization with saved preference BEFORE ThemeService
        LocalizationService.Instance.Initialize(prefs.Language);

        ThemeService.Instance.Initialize(prefs);
        RegisterAntiCrashSystem();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();

        base.OnFrameworkInitializationCompleted();
    }

    private void RegisterAntiCrashSystem()
    {
        TaskScheduler.UnobservedTaskException += (sender, e) =>
        {
            var exception = e.Exception.InnerException ?? e.Exception;

            if (exception is TaskCanceledException ||
                exception is OperationCanceledException ||
                exception is HttpRequestException ||
                exception is SocketException ||
                exception is TimeoutException)
            {
                e.SetObserved();
                Log.Warning(exception, "Anti-Crash: Swallowed a benign network/cancellation async task exception.");
                NullActionLogger.Error("Global_AsyncEngine", exception, "Background network/cancellation exception intercepted and suppressed.");
            }
            else
            {
                Log.Fatal(exception, "Anti-Crash: CRITICAL unobserved async task exception. App state may be corrupted.");
                NullActionLogger.Error("Global_CriticalCore", exception, "Fatal background async loop exception intercepted. Allowing crash.");
            }
        };

        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            if (e.ExceptionObject is Exception exception)
            {
                Log.Fatal(exception, "Anti-Crash: Critical unhandled domain exception. IsTerminating: {IsTerminating}", e.IsTerminating);
                NullActionLogger.Error("Global_CriticalCore", exception, $"Fatal application boundary crash intercepted. IsTerminating={e.IsTerminating}");

                if (e.IsTerminating)
                    Log.Information("NullWave is shutting down due to a fatal environment failure. Emergency cleanup executed.");
            }
        };
    }
}