using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Display;

namespace NullWave.Helpers.Logging;

/// <summary>A single buffered log line with its level, for the in-app log viewer.</summary>
public sealed record InAppLogEntry(LogEventLevel Level, string Line);

public class InAppLogSink : ILogEventSink
{
    private static readonly ConcurrentQueue<InAppLogEntry> _logLines = new();
    private readonly MessageTemplateTextFormatter _formatter;

    // Throttle UI updates to max 1 time per second
    private static long _lastFlushTicks = DateTime.UtcNow.Ticks;
    private static readonly long FlushIntervalTicks = TimeSpan.FromSeconds(1).Ticks;

    public const int MaxBufferedLines = 500;

    public static event Action? LogUpdated;

    public InAppLogSink(string outputTemplate)
    {
        _formatter = new MessageTemplateTextFormatter(outputTemplate, null);
    }

    public static string GetSnapshot() => string.Join(Environment.NewLine, _logLines.Select(e => e.Line));

    /// <summary>Snapshot for the log viewer: level + rendered line.</summary>
    public static InAppLogEntry[] GetEntries() => _logLines.ToArray();

    public void Emit(LogEvent logEvent)
    {
        using var writer = new StringWriter();
        _formatter.Format(logEvent, writer);

        _logLines.Enqueue(new InAppLogEntry(logEvent.Level, writer.ToString().TrimEnd()));

        while (_logLines.Count > MaxBufferedLines)
        {
            _logLines.TryDequeue(out _);
        }

        // Only notify UI if enough time has passed
        var now = DateTime.UtcNow.Ticks;
        if (now - Interlocked.Read(ref _lastFlushTicks) >= FlushIntervalTicks)
        {
            Interlocked.Exchange(ref _lastFlushTicks, now);
            LogUpdated?.Invoke();
        }
    }
}