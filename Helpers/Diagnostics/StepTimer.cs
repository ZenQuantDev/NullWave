using System.Diagnostics;
using System.Text;
using Serilog;

namespace NullWave.Helpers.Diagnostics;

/// <summary>Logs a [SLOW] line only when a block took long, with per-step timings.</summary>
public sealed class StepTimer
{
    private readonly string _name;
    private readonly Stopwatch _sw = Stopwatch.StartNew();
    private readonly StringBuilder _steps = new();
    private long _last;

    public StepTimer(string name) => _name = name;

    public void Step(string label)
    {
        var now = _sw.ElapsedMilliseconds;
        if (now - _last >= 20) _steps.Append(' ').Append(label).Append('=').Append(now - _last).Append("ms");
        _last = now;
    }

    public void Done()
    {
        var total = _sw.ElapsedMilliseconds;
        if (total >= 100) Log.Warning("[SLOW] {Name} took {Total} ms:{Steps}", _name, total, _steps.ToString());
    }
}