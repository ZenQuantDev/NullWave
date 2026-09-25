using System;
using System.Threading.Tasks;
using System.Reflection;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using NullWave.Views;
using NullWave.Helpers;
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
        {
            var mainWindow = new MainWindow();
            desktop.MainWindow = mainWindow;

            // "What's New" screen: shown as an owned modal dialog AFTER the main window
            // is visible. An ownerless window shown at startup breaks activation/z-order,
            // misbehaves with Win+D / Alt+Tab, and glitches MainWindow during drags.
            var prefs = new PreferencesService();
            var currentVersion = GetAppVersion();

            if (prefs.Current.LastSeenVersion != currentVersion)
            {
                prefs.Update(p => p.LastSeenVersion = currentVersion);
                mainWindow.Opened += (_, _) =>
                {
                    var whatsNew = new WhatsNewWindow(currentVersion);
                    _ = whatsNew.ShowDialog(mainWindow);
                };
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static string GetAppVersion()
    {
        var info = typeof(App).Assembly
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            var plus = info.IndexOf('+');
            if (plus > 0) info = info[..plus];
            return info;
        }
        return typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
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

                // Generate user-friendly crash report before the app dies
                if (e.IsTerminating)
                {
                    CrashHandler.HandleFatalCrash(exception);
                    Log.Information("NullWave is shutting down due to a fatal environment failure. Emergency cleanup executed.");
                }
            }
        };
    }
}