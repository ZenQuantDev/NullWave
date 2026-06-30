using System;
using Avalonia;
using Material.Icons.Avalonia;
using NullWave.Helpers;
using NullWave.Helpers.Logging;
using NullWave.Services;
using NullWave.Models;
using Serilog;

namespace NullWave;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        //  1. Ensure ~/.nullwave/* directories exist 
        NullWavePaths.EnsureDirectories();

        //  2. Initialize Serilog before anything else 
        NullWaveLogConfig.Initialize();

        try
        {
            var appBuilder = BuildAvaloniaApp();

            // Fire validation visual checks as soon as initialization is complete
            ToastService.Instance.Show("Welcome to NullWave! Library initialized successfully.", ToastType.Success, 5000);
            ToastService.Instance.Show("Failed to sync local AI models. Using local cache fallback.", ToastType.Error, 6000);

            appBuilder.StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            NullActionLogger.Error("Program", ex, "Unhandled top-level exception");
            throw;
        }
        finally
        {
            NullWaveLogConfig.CloseAndFlush();
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}