using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace NullWave.Helpers.Attached;

/// <summary>
/// OLED anti-retention: periodically nudges a container by 1-2px via
/// RenderTransform (never Margin/Width - those trigger layout, this doesn't).
/// Imperceptible during use; continuously alters pixel luminance boundaries.
/// </summary>
public class PixelShiftBehavior
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<PixelShiftBehavior, Control, bool>("IsEnabled", false);

    public static bool GetIsEnabled(Control c) => c.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(Control c, bool value) => c.SetValue(IsEnabledProperty, value);

    private static readonly TimeSpan ShiftInterval = TimeSpan.FromMinutes(4);
    private sealed class Target
    {
        public WeakReference<Control> Control { get; }
        public ITransform? OriginalTransform { get; }
        public TranslateTransform Shift { get; } = new();
        public TransformGroup? CombinedTransform { get; set; }
        public ITransform? AppliedTransform { get; set; }

        public Target(Control control)
        {
            Control = new WeakReference<Control>(control);
            OriginalTransform = control.RenderTransform;
        }
    }

    private static readonly object TargetsLock = new();
    private static readonly List<Target> Targets = new();
    private static readonly Random Rng = new();
    private static DispatcherTimer? _timer;

    static PixelShiftBehavior()
    {
        IsEnabledProperty.Changed.AddClassHandler<Control>(OnIsEnabledChanged);
    }

    private static void OnIsEnabledChanged(Control c, AvaloniaPropertyChangedEventArgs e)
    {
        void Update()
        {
            lock (TargetsLock)
            {
                RemoveDeadTargets();
                if (e.NewValue is true)
                {
                    if (!Contains(c)) Targets.Add(new Target(c));
                    EnsureTimer();
                }
                else
                {
                    Remove(c);
                    StopTimerIfEmpty();
                }
            }
        }

        if (Dispatcher.UIThread.CheckAccess()) Update();
        else Dispatcher.UIThread.Post(Update);
    }

    private static void Timer_Tick(object? sender, EventArgs e)
    {
        lock (TargetsLock)
        {
            for (var i = Targets.Count - 1; i >= 0; i--)
            {
                var target = Targets[i];
                if (!target.Control.TryGetTarget(out var control))
                {
                    Targets.RemoveAt(i);
                    continue;
                }
                if (!control.IsVisible) continue;

                target.Shift.X = Rng.Next(0, 3);
                target.Shift.Y = Rng.Next(0, 3);

                if (target.OriginalTransform is Transform original)
                {
                    if (target.CombinedTransform == null)
                    {
                        target.CombinedTransform = new TransformGroup();
                        target.CombinedTransform.Children.Add(original);
                        target.CombinedTransform.Children.Add(target.Shift);
                    }
                    target.AppliedTransform = target.CombinedTransform;
                }
                else
                {
                    target.AppliedTransform = target.Shift;
                }

                if (!ReferenceEquals(control.RenderTransform, target.AppliedTransform))
                    control.RenderTransform = target.AppliedTransform;
            }

            StopTimerIfEmpty();
        }
    }

    private static bool Contains(Control control)
    {
        foreach (var target in Targets)
            if (target.Control.TryGetTarget(out var existing) && ReferenceEquals(existing, control))
                return true;
        return false;
    }

    private static void Remove(Control control)
    {
        for (var i = Targets.Count - 1; i >= 0; i--)
        {
            var target = Targets[i];
            if (!target.Control.TryGetTarget(out var existing))
            {
                Targets.RemoveAt(i);
                continue;
            }
            if (!ReferenceEquals(existing, control)) continue;

            if (ReferenceEquals(control.RenderTransform, target.AppliedTransform))
                control.RenderTransform = target.OriginalTransform;
            Targets.RemoveAt(i);
        }
    }

    private static void RemoveDeadTargets()
    {
        for (var i = Targets.Count - 1; i >= 0; i--)
            if (!Targets[i].Control.TryGetTarget(out _)) Targets.RemoveAt(i);
    }

    private static void EnsureTimer()
    {
        if (_timer != null) return;
        _timer = new DispatcherTimer { Interval = ShiftInterval };
        _timer.Tick += Timer_Tick;
        _timer.Start();
    }

    private static void StopTimerIfEmpty()
    {
        if (Targets.Count != 0 || _timer == null) return;
        _timer.Stop();
        _timer.Tick -= Timer_Tick;
        _timer = null;
    }
}