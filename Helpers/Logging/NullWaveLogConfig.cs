using System;
using System.IO;
using Serilog;
using Serilog.Events;
using Serilog.Filters;

namespace NullWave.Helpers.Logging;

/// <summary>
/// Configures Serilog with three separate file sinks:
///
///   ~/.nullwave/logs/NullWave-YYYYMMDD.log       ← everything (system + general)
///   ~/.nullwave/logs/UserActions-YYYYMMDD.log    ← [ACTION] entries only
///   ~/.nullwave/logs/Errors-YYYYMMDD.log         ← errors with source attribution
///
/// Call NullWaveLogConfig.Initialize() as the very first line of Program.cs,
/// before any services are constructed.
/// </summary>
public static class NullWaveLogConfig
{
    public static void Initialize()
    {
        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nullwave", "logs");

        Directory.CreateDirectory(logDir);

        var outputTemplate =
            "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}";

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()

            // ── Sink 1: Main log — all events ─────────────────────────────────
            .WriteTo.File(
                path: Path.Combine(logDir, "NullWave-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                outputTemplate: outputTemplate)

            // ── Sink 2: Console (debug builds) ────────────────────────────────
            .WriteTo.Console(outputTemplate: outputTemplate)

            // ── Sink 3: UserActions — only entries with Channel=UserAction ────
            .WriteTo.Logger(lc => lc
                .Filter.ByIncludingOnly(
                    Matching.WithProperty<string>(
                        "Channel", v => v == "UserAction"))
                .WriteTo.File(
                    path: Path.Combine(logDir, "UserActions-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30,
                    outputTemplate: outputTemplate))

            // ── Sink 4: Errors — Error level and above ────────────────────────
            .WriteTo.Logger(lc => lc
                .MinimumLevel.Error()
                .WriteTo.File(
                    path: Path.Combine(logDir, "Errors-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30,
                    outputTemplate: outputTemplate))

            .CreateLogger();

        Log.Information("Serilog initialized — logs at {LogDir}", logDir);
    }

    public static void CloseAndFlush()
        => Log.CloseAndFlush();
}