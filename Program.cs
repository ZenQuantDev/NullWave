using System;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using NullWave.Views;
using NullWave.Helpers;
using NullWave.Helpers.Logging;
using NullWave.Services;
using Serilog;

namespace NullWave;

class Program
{
    private static FileStream? _singleInstanceLock;

    [STAThread]
    public static void Main(string[] args)
    {
        NullWavePaths.EnsureDirectories();

        if (!TryAcquireSingleInstanceLock())
        {
            Console.WriteLine("NullWave is already running.");
            return;
        }

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

    private static bool TryAcquireSingleInstanceLock()
    {
        try
        {
            _singleInstanceLock = new FileStream(
                Path.Combine(NullWavePaths.DataDir, "single.instance.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}