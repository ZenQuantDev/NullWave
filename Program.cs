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
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

        if (OperatingSystem.IsWindows())
        {
            // NULLWAVE_RENDER=software -> CPU rendering for GPU-less floor machines (i3 380M).
            var forceSoftware = string.Equals(
                Environment.GetEnvironmentVariable("NULLWAVE_RENDER"), "software",
                StringComparison.OrdinalIgnoreCase);

            // Compose mode: lowlatency (default) correctly V-Syncs at display refresh rate.
            // direct bypasses V-Sync entirely and produces uncapped render storms (confirmed
            // 40,000+ fps CPU churn) - do not use as a "smoother" alternative, it's the opposite.
            var compose = (Environment.GetEnvironmentVariable("NULLWAVE_COMPOSE") ?? "lowlatency").Trim().ToLowerInvariant();

            if (forceSoftware)
            {
                builder.With(new Win32PlatformOptions
                {
                    RenderingMode = new[] { Win32RenderingMode.Software }
                });
            }
            else
            {
                Win32CompositionMode[] composition = compose switch
                {
                    "direct" => new[] { Win32CompositionMode.DirectComposition, Win32CompositionMode.WinUIComposition, Win32CompositionMode.LowLatencyDxgiSwapChain },
                    "winui"  => new[] { Win32CompositionMode.WinUIComposition, Win32CompositionMode.DirectComposition, Win32CompositionMode.LowLatencyDxgiSwapChain },
                    _        => new[] { Win32CompositionMode.LowLatencyDxgiSwapChain, Win32CompositionMode.DirectComposition, Win32CompositionMode.WinUIComposition },
                };

                builder.With(new Win32PlatformOptions
                {
                    RenderingMode = new[] { Win32RenderingMode.AngleEgl, Win32RenderingMode.Wgl, Win32RenderingMode.Software },
                    CompositionMode = composition
                });
            }

            Log.Information("[STARTUP] Render config: software={Software}, compose={Compose}", forceSoftware, compose);
        }

        return builder;
    }
}