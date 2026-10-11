using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Serilog;

namespace NullWave.Helpers.Diagnostics;

/// <summary>
/// Records named moments during startup, each as milliseconds since the operating system created
/// the process. One summary line at the end of startup shows where the time went:
///
///   [STARTUP-TIME] Main entered 95 ms | Framework initialized 610 ms | MainWindow opened 1180 ms | ...
///
/// Marks made before the logger exists (the first one or two) are not logged on their own, which is
/// why LogSummary() prints every mark at once.
/// </summary>
public static class StartupTimeline
{
    private static readonly object Gate = new();
    private static readonly List<(string Name, long Ms)> Marks = new();
    private static Func<long> _clock = CreateProcessClock();

    private static Func<long> CreateProcessClock()
    {
        long offsetMs = 0;
        try
        {
            using var self = Process.GetCurrentProcess();
            offsetMs = Math.Max(0, (long)(DateTime.Now - self.StartTime).TotalMilliseconds);
        }
        catch
        {
            // Process start time unavailable: marks then count from the first use of this class.
        }

        var watch = Stopwatch.StartNew();
        return () => offsetMs + watch.ElapsedMilliseconds;
    }

    /// <summary>Records a moment. Cheap enough to call from anywhere on the startup path.</summary>
    public static void Mark(string name)
    {
        long ms;
        lock (Gate)
        {
            ms = _clock();
            Marks.Add((name, ms));
        }

        Log.Debug("[STARTUP-TIME] {Ms,6} ms  {Name}", ms, name);
    }

    public static IReadOnlyList<(string Name, long Ms)> Snapshot()
    {
        lock (Gate) return Marks.ToArray();
    }

    /// <summary>All marks on one line, in the order they were made.</summary>
    public static string Summary()
        => string.Join(" | ", Snapshot().Select(m => $"{m.Name} {m.Ms} ms"));

    public static void LogSummary() => Log.Information("[STARTUP-TIME] {Summary}", Summary());

    internal static void ResetForTests(Func<long>? clock = null)
    {
        lock (Gate)
        {
            Marks.Clear();
            _clock = clock ?? CreateProcessClock();
        }
    }
}