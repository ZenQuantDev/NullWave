using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using NullWave.Views;
using NullWave.Helpers.Logging;
using NullWave.Services;
using Serilog;

namespace NullWave;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        // Boot the live theme engine BEFORE any window is constructed so the
        // first frame already uses the saved accent/scale/density.
        ThemeService.Instance.Initialize(new PreferencesService().Current);

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
            e.SetObserved();
            var exception = e.Exception.InnerException ?? e.Exception;
            Log.Error(exception, "Anti-Crash: Swallowed an unobserved async task exception.");
            NullActionLogger.Error("Global_AsyncEngine", exception, "Background async loop exception intercepted and suppressed.");
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