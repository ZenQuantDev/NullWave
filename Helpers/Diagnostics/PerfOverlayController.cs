using System;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Rendering;
using Serilog;

namespace NullWave.Helpers.Diagnostics;

/// <summary>
/// Single owner of the F3 performance-overlay + PerfLogger lifecycle, so EVERY
/// window (Main, Settings, Profile) can toggle debugging with the same hotkey.
/// Overlays are per-TopLevel (RendererDiagnostics); the logger attaches to the
/// main window's render loop. Tag provider is supplied by MainWindow (it owns
/// page/sidebar/hover context) and reused regardless of which window toggles.
/// </summary>
public static class PerfOverlayController
{
    private static PerfLogger? _logger;
    private static Func<string>? _tagProvider;

    public static void SetTagProvider(Func<string> provider) => _tagProvider = provider;

    public static void Toggle()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop
            || desktop.Windows.Count == 0)
            return;

        bool currentlyOff = desktop.Windows[0].RendererDiagnostics.DebugOverlays == RendererDebugOverlays.None;

        var next = currentlyOff
            ? RendererDebugOverlays.Fps
              | RendererDebugOverlays.RenderTimeGraph
              | RendererDebugOverlays.LayoutTimeGraph
              | RendererDebugOverlays.DirtyRects
            : RendererDebugOverlays.None;

        foreach (var w in desktop.Windows)
            w.RendererDiagnostics.DebugOverlays = next;

        if (next != RendererDebugOverlays.None)
        {
            if (_logger == null && desktop.MainWindow != null)
            {
                _logger = new PerfLogger(desktop.MainWindow)
                {
                    TagProvider = _tagProvider ?? (() => "page=unknown|hover=none|sidebar=unknown")
                };
            }
            _logger?.Start();
        }
        else
        {
            _logger?.Stop();
        }

        Log.Information(next == RendererDebugOverlays.None
            ? "[DevTools] Performance overlays DISABLED"
            : "[DevTools] Performance overlays ENABLED");
    }
}