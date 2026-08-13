using System;
using System.IO;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Filters;

namespace NullWave.Helpers.Logging;

public static class NullWaveLogConfig
{
    public static readonly LoggingLevelSwitch LevelSwitch = new();

    public static void Initialize(bool useVerbose)
    {
        LevelSwitch.MinimumLevel = useVerbose ? LogEventLevel.Debug : LogEventLevel.Information;

        // FIX: Use the same centralized path manager as the rest of the app!
        // (Use AppDataDir, DataDir, or BaseDir depending on what your NullWavePaths class exposes)
        var logDir = Path.Combine(NullWavePaths.DataDir, "logs"); 
        
        Directory.CreateDirectory(logDir);

        var outputTemplate =
            "{Timestamp:dd-MM-yyyy HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}";

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.ControlledBy(LevelSwitch)
            .Enrich.FromLogContext()
            .Enrich.With<ApiKeyRedactionEnricher>()

            .WriteTo.File(
                path: Path.Combine(logDir, "NullWave-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                fileSizeLimitBytes: 10 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                outputTemplate: outputTemplate)

            .WriteTo.Sink(new InAppLogSink(outputTemplate))

            .WriteTo.Console(outputTemplate: outputTemplate)

            .WriteTo.Logger(lc => lc
                .Filter.ByIncludingOnly(Matching.WithProperty<string>("Channel", v => v == "UserAction"))
                .WriteTo.File(
                    path: Path.Combine(logDir, "UserActions-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14,
                    fileSizeLimitBytes: 5 * 1024 * 1024,
                    rollOnFileSizeLimit: true,
                    outputTemplate: outputTemplate))

            .WriteTo.Logger(lc => lc
                .MinimumLevel.Error()
                .WriteTo.File(
                    path: Path.Combine(logDir, "Errors-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14,
                    fileSizeLimitBytes: 5 * 1024 * 1024,
                    rollOnFileSizeLimit: true,
                    outputTemplate: outputTemplate))
            .CreateLogger();

        Log.Information("Serilog initialized under dynamic operational modes.");
    }

    public static void CloseAndFlush() => Log.CloseAndFlush();
}