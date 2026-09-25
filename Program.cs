using System;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Win32;
using NullWave.Views;
using NullWave.Helpers;
using NullWave.Helpers.Logging;
using NullWave.Services;
using Serilog;
using Velopack;

namespace NullWave;

class Program
{
    private static FileStream? _singleInstanceLock;

    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build().Run();

        NullWavePaths.EnsureDirectories();

        // Restrict data directory permissions on Linux (owner-only access)
        if (OperatingSystem.IsLinux())
        {
            try
            {
                File.SetUnixFileMode(NullWavePaths.DataDir,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                Log.Debug("[Program] Set restrictive permissions on data directory");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[Program] Could not set restrictive permissions on data directory");
            }
        }

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
            // Catch synchronous startup crashes (like MainViewModel constructor failures)
            NullActionLogger.Error("Program", ex, "Unhandled top-level exception");
            CrashHandler.HandleFatalCrash(ex);
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

    /* 
        This part of code works better on systems with GPU acceleration. It resolves problems with Main Window moving animation and stuttering.
        But it may not work on older systems without GPU like i3 380M, which is the "floor machine" for NulLWave.
        For now NullWave will be using "old" style system, and in future updates we will add a setting to switch between these two modes, 
        and make overall performance better for all users. 
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new Win32PlatformOptions
            {
                RenderingMode = new[] { Win32RenderingMode.Software }
            })
            .WithInterFont()
            .LogToTrace();
            */
}