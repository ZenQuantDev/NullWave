using System;
using System.IO;
using System.Text.RegularExpressions;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace NullWave.Helpers;

// Scrubs API keys and secrets from log output
public class RedactionEnricher : ILogEventEnricher
{
    // Matches typical API key patterns: 20+ alphanumeric/dash/underscore chars
    private static readonly Regex ApiKeyPattern = new(
        @"[A-Za-z0-9\-_]{20,}",
        RegexOptions.Compiled);

    // Known safe values that should never be redacted (add more as needed)
    private static readonly string[] SafeValues =
    {
        "NullWave", "Unknown", "YouTube", "Spotify", "SoundCloud", "Local"
    };

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory factory)
    {
        // Rewrite the message template properties
        foreach (var property in logEvent.Properties)
        {
            if (property.Value is ScalarValue scalar &&
                scalar.Value is string str &&
                ShouldRedact(str))
            {
                var redacted = Redact(str);
                logEvent.AddOrUpdateProperty(
                    factory.CreateProperty(property.Key,
                        new ScalarValue(redacted)));
            }
        }
    }

    private static bool ShouldRedact(string value)
    {
        if (value.Length < 20) return false;
        foreach (var safe in SafeValues)
            if (value.Equals(safe, StringComparison.OrdinalIgnoreCase))
                return false;
        return ApiKeyPattern.IsMatch(value);
    }

    private static string Redact(string value)
    {
        return ApiKeyPattern.Replace(value, match =>
        {
            var m = match.Value;
            if (m.Length < 20) return m;
            // Show first 4 and last 4 chars only: AIza...tWtE
            return $"{m[..4]}...{m[^4..]}";
        });
    }
}

public static class AppLogger
{
    public static void Initialize()
    {
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nullwave", "logs", "nullwave-.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.With<RedactionEnricher>()
            .WriteTo.Console(outputTemplate:
                "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate:
                "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Log.Information("NullWave started — logs stored at {Path}",
            Path.GetDirectoryName(logPath));
    }

    public static void Shutdown() => Log.CloseAndFlush();
}