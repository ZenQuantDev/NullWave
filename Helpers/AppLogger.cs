using System;
using System.IO;
using Serilog;

namespace NullWave.Helpers;

public static class AppLogger
{
    public static void Initialize()
    {
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nullwave", "logs", "nullwave-.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console()
            .WriteTo.File(logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .CreateLogger();

        Log.Information("NullWave started");
    }

    public static void Shutdown() => Log.CloseAndFlush();
}