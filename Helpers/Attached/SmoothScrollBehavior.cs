using System;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace NullWave.Helpers.Attached;

/// <summary>
/// Inertial smooth scrolling driven by TopLevel.RequestAnimationFrame (display-synced).
/// Critically damped spring, fixed-substep integration => identical trajectory at any
/// frame rate. External scrolls (scrollbar drag, keyboard) are detected by VALUE
/// matching against our last written offset, not by a re-entrancy flag: ScrollChanged
/// for our own writes can arrive deferred, and a flag-based guard misreads those as
/// external input and kills the animation after one frame ("barely moves" bug).
/// </summary>
public class SmoothScrollBehavior
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<SmoothScrollBehavior, ScrollViewer, bool>("IsEnabled", false);

    public static bool GetIsEnabled(ScrollViewer scrollViewer) => scrollViewer.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(ScrollViewer scrollViewer, bool value) => scrollViewer.SetValue(IsEnabledProperty, value);

    // Wheel notch -> target pixels. If one notch still feels short after this fix,
    // raise to 160-200; this is the single tuning constant for distance.
    private const double PixelsPerNotch = 120;

    // Critically damped spring: c = 2*sqrt(k) settles fastest without overshoot.
    private const double Stiffness = 170.0;
    private const double Damping = 26.0;

    private const double SnapDistance = 0.5;    // px
    private const double SnapVelocity = 1.0;    // px/s
    private const double MaxSubstep = 1.0 / 120.0;
    private const double OwnWriteTolerance = 1.0; // px epsilon for value matching

    static SmoothScrollBehavior()
    {
        IsEnabledProperty.Changed.AddClassHandler<ScrollViewer>(OnIsEnabledChanged);
    }

    private sealed class State
    {
        public double Target;
        public double Position;
        public double Velocity;
        public bool Animating;
        public double LastWrittenY = double.NaN; // value-match anchor for our own writes
        public TimeSpan? LastFrameTime;
        
        // Throttle state for external drag (coalesces 1000Hz Win32 mouse events)
        public double PendingExternalTarget;
        public bool ExternalScrollScheduled;
    }

    private static readonly ConditionalWeakTable<ScrollViewer, State> States = new();

    private static void OnIsEnabledChanged(ScrollViewer sv, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is true) Attach(sv);
        else Detach(sv);
    }

    private static void Attach(ScrollViewer sv)
    {
        States.AddOrUpdate(sv, new State { Target = sv.Offset.Y, Position = sv.Offset.Y });
        sv.AddHandler(InputElement.PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel, handledEventsToo: true);
        sv.ScrollChanged += OnExternalScroll;
    }

    private static void Detach(ScrollViewer sv)
    {
        sv.RemoveHandler(InputElement.PointerWheelChangedEvent, OnWheel);
        sv.ScrollChanged -= OnExternalScroll;
        States.Remove(sv);
    }

    /// <summary>
    /// Value-match ownership: if the current offset equals what we last wrote (within
    /// tolerance), this ScrollChanged is our own - synchronous or deferred - so ignore
    /// it. Any other offset (scrollbar drag, keyboard, focus/BringIntoView) is external.
    /// We immediately kill any running spring animation to prevent fighting, then 
    /// frame-throttle the state commit to coalesce high-frequency Win32 drag events.
    /// </summary>
    private static void OnExternalScroll(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer sv || !States.TryGetValue(sv, out var s)) return;

        if (!double.IsNaN(s.LastWrittenY) &&
            Math.Abs(sv.Offset.Y - s.LastWrittenY) <= OwnWriteTolerance)
            return; // our own write

        // Immediately kill any running spring animation so it doesn't fight the scrollbar drag
        s.Animating = false;
        s.Velocity = 0;

        // Frame-throttle the state commit to coalesce 1000Hz Win32 mouse events
        s.PendingExternalTarget = sv.Offset.Y;
        if (!s.ExternalScrollScheduled)
        {
            s.ExternalScrollScheduled = true;
            Dispatcher.UIThread.Post(() =>
            {
                if (States.TryGetValue(sv, out var state))
                {
                    state.Target = state.PendingExternalTarget;
                    state.Position = state.PendingExternalTarget;
                    state.ExternalScrollScheduled = false;
                }
            }, DispatcherPriority.Render);
        }
    }

    private static void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (sender is not ScrollViewer sv || !GetIsEnabled(sv)) return;
        if (!States.TryGetValue(sv, out var s)) return;

        var maxY = Math.Max(0, sv.Extent.Height - sv.Viewport.Height);
        if (maxY <= 0) return;

        e.Handled = true;

        // Resync position if content extent changed under us (filter/refresh).
        s.Position = sv.Offset.Y;
        s.Target = Math.Clamp(s.Target - e.Delta.Y * PixelsPerNotch, 0, maxY);

        if (!s.Animating)
        {
            s.Animating = true;
            s.Velocity = 0;
            s.LastFrameTime = null; // first frame uses a sane dt guess, no stale jump
            RequestFrame(sv, s);
        }
    }

    private static void RequestFrame(ScrollViewer sv, State s)
    {
        var topLevel = TopLevel.GetTopLevel(sv);
        if (topLevel == null) { s.Animating = false; return; }
        topLevel.RequestAnimationFrame(t => Tick(sv, s, t));
    }

    private static void Tick(ScrollViewer sv, State s, TimeSpan frameTime)
    {
        if (!s.Animating) return;

        var dt = s.LastFrameTime is { } last ? (frameTime - last).TotalSeconds : 1.0 / 60.0;
        s.LastFrameTime = frameTime;
        dt = Math.Clamp(dt, 0, 0.1); // hitch / minimized-window guard

        var maxY = Math.Max(0, sv.Extent.Height - sv.Viewport.Height);

        // Sub-stepped semi-implicit Euler: stable and frame-rate independent.
        var remaining = dt;
        while (remaining > 0)
        {
            var h = Math.Min(MaxSubstep, remaining);
            remaining -= h;

            var accel = -Stiffness * (s.Position - s.Target) - Damping * s.Velocity;
            s.Velocity += accel * h;
            s.Position += s.Velocity * h;

            // Clamp at edges, kill velocity into the wall (no bounce).
            if (s.Position < 0) { s.Position = 0; s.Velocity = Math.Max(0, s.Velocity); }
            else if (s.Position > maxY) { s.Position = maxY; s.Velocity = Math.Min(0, s.Velocity); }
        }

        if (Math.Abs(s.Target - s.Position) < SnapDistance && Math.Abs(s.Velocity) < SnapVelocity)
        {
            s.Position = s.Target;
            s.Velocity = 0;
            s.Animating = false;
            WriteOffset(sv, s, s.Position);
            return;
        }

        WriteOffset(sv, s, s.Position);
        RequestFrame(sv, s);
    }

    private static void WriteOffset(ScrollViewer sv, State s, double y)
    {
        s.LastWrittenY = y;
        sv.Offset = new Vector(sv.Offset.X, y);
    }
}