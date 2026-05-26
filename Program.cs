using Avalonia;
using NullWave.Helpers;
using System;

namespace NullWave;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        AppLogger.Initialize();
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Serilog.Log.Fatal(ex, "Application crashed");
        }
        finally
        {
            AppLogger.Shutdown();
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            #if DEBUG
            .WithDeveloperTools()
            #endif
            .WithInterFont()
            .LogToTrace();
}