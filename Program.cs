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
        NullWavePaths.EnsureDirectories();
        
        var prefsService = new PreferencesService();
        NullWaveLogConfig.Initialize(prefsService.Current.VerboseLogging);

        try
        {
            var appBuilder = BuildAvaloniaApp();
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