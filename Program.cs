using System;
using Avalonia;
using NullWave.Helpers;
using NullWave.Helpers.Logging;
using Serilog;

namespace NullWave;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // ── 1. Ensure ~/.nullwave/* directories exist ─────────────────────────
        NullWavePaths.EnsureDirectories();

        // ── 2. Initialize Serilog before anything else ────────────────────────
        //      This must be first so every downstream exception is captured.
        NullWaveLogConfig.Initialize();

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            // Last-resort catch — any unhandled exception from the UI thread
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