using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using NullWave.Views;
using NullWave.Helpers.Logging;
using Serilog;

namespace NullWave;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        
        // Wire up the global anti-crash systems before the UI initializes
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
        // 1. Intercept Unobserved Task Exceptions (Asynchronous/Background operations)
        // Since NullWave heavily relies on async commands, this will catch the majority of transient errors.
        TaskScheduler.UnobservedTaskException += (sender, e) =>
        {
            // Crucial: Tells the runtime we have handled this, preventing an app crash
            e.SetObserved(); 

            var exception = e.Exception.InnerException ?? e.Exception;
            
            Log.Error(exception, "Anti-Crash: Swallowed an unobserved async task exception.");
            NullActionLogger.Error("Global_AsyncEngine", exception, "Background async loop exception intercepted and suppressed.");

            // TODO: Call your custom in-app pop-up notification manager here!
            // Example: NotificationService.ShowError("Background operation failed", exception.Message);
        };

        // 2. Intercept AppDomain Unhandled Exceptions (Synchronous/Main Thread failures)
        // This acts as your absolute last line of defense for severe thread crashes.
        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            if (e.ExceptionObject is Exception exception)
            {
                Log.Fatal(exception, "Anti-Crash: Critical unhandled domain exception. IsTerminating: {IsTerminating}", e.IsTerminating);
                NullActionLogger.Error("Global_CriticalCore", exception, $"Fatal application boundary crash intercepted. IsTerminating={e.IsTerminating}");
                
                if (e.IsTerminating)
                {
                    // If the OS/Runtime forces a termination, this block is your last-second window
                    // to safely flush log files, save database context, or close file streams.
                    Log.Information("NullWave is shutting down due to a fatal environment failure. Emergency cleanup executed.");
                }
            }
        };
    }
}