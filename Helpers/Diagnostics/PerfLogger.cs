using System;
using Avalonia.Controls;
using Serilog;

namespace NullWave.Helpers.Diagnostics;

/// <summary>
/// Aggregated per-second frame timing to the Perf channel. Never logs per-frame.
/// v2: stall counters make felt stutter visible in the log itself - an average
/// fps can look fine while containing a 250ms freeze, so we count frames over
/// 33ms (sub-30fps moment) and over 100ms (hard freeze) per window.
/// </summary>
public sealed class PerfLogger
{
    private readonly TopLevel _topLevel;
    private bool _running;
    private TimeSpan? _lastFrame;
    private TimeSpan _windowStart;
    private int _frameCount;
    private int _stall33;
    private int _stall100;
    private double _minMs = double.MaxValue, _maxMs, _sumMs;

    public Func<string>? TagProvider { get; set; }

    public PerfLogger(TopLevel topLevel) => _topLevel = topLevel;

    public void Start()
    {
        if (_running) return;
        _running = true;
        _lastFrame = null;
        ResetWindow(TimeSpan.Zero);
        _topLevel.RequestAnimationFrame(Tick);
    }

    public void Stop() => _running = false;

    private void ResetWindow(TimeSpan now)
    {
        _windowStart = now;
        _frameCount = 0;
        _stall33 = 0;
        _stall100 = 0;
        _minMs = double.MaxValue;
        _maxMs = 0;
        _sumMs = 0;
    }

    private void Tick(TimeSpan now)
    {
        if (!_running) return;

        if (_lastFrame is { } last)
        {
            var deltaMs = (now - last).TotalMilliseconds;
            _frameCount++;
            _sumMs += deltaMs;
            if (deltaMs < _minMs) _minMs = deltaMs;
            if (deltaMs > _maxMs) _maxMs = deltaMs;
            if (deltaMs > 33.3) _stall33++;
            if (deltaMs > 100.0) _stall100++;
        }
        _lastFrame = now;

        if ((now - _windowStart).TotalSeconds >= 1.0 && _frameCount > 0)
        {
            var avgMs = _sumMs / _frameCount;
            var fps = 1000.0 / avgMs;
            var tag = TagProvider?.Invoke() ?? "unknown";

            Log.ForContext("Channel", "Perf").Information(
                "{Tag} frames={Frames} fps={Fps:F1} deltaMs(min/avg/max)={Min:F1}/{Avg:F1}/{Max:F1} stalls>33ms={S33} stalls>100ms={S100}",
                tag, _frameCount, fps, _minMs, avgMs, _maxMs, _stall33, _stall100);

            ResetWindow(now);
        }

        _topLevel.RequestAnimationFrame(Tick);
    }
}