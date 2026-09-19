using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Display;

namespace NullWave.Helpers.Logging;

public class InAppLogSink : ILogEventSink
{
    private static readonly ConcurrentQueue<string> _logLines = new();
    private readonly MessageTemplateTextFormatter _formatter;
    
    // Throttle UI updates to max 1 time per second
    private static long _lastFlushTicks = DateTime.UtcNow.Ticks; 
    private static readonly long FlushIntervalTicks = TimeSpan.FromSeconds(1).Ticks;

    public static event Action? LogUpdated;

    public InAppLogSink(string outputTemplate)
    {
        _formatter = new MessageTemplateTextFormatter(outputTemplate, null);
    }

    public static string GetSnapshot() => string.Join(Environment.NewLine, _logLines);

    public void Emit(LogEvent logEvent)
    {
        using var writer = new StringWriter();
        _formatter.Format(logEvent, writer);
        
        _logLines.Enqueue(writer.ToString().TrimEnd());

        while (_logLines.Count > 100) // Keep buffer manageable
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