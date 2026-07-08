using System;
using System.Collections.Concurrent;
using System.IO;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Display;

namespace NullWave.Helpers.Logging;

public class InAppLogSink : ILogEventSink
{
    private static readonly ConcurrentQueue<string> _logLines = new();
    private readonly MessageTemplateTextFormatter _formatter;

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

        while (_logLines.Count > 50)
        {
            _logLines.TryDequeue(out _);
        }

        LogUpdated?.Invoke();
    }
}