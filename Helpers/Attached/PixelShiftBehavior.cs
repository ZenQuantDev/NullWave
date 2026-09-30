using System;
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
    private static readonly Random Rng = new();
    private static DispatcherTimer? _timer;

    static PixelShiftBehavior()
    {
        IsEnabledProperty.Changed.AddClassHandler<Control>(OnIsEnabledChanged);
    }

    private static void OnIsEnabledChanged(Control c, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            // Use a single shared timer for all attached controls to save resources
            if (_timer == null)
            {
                _timer = new DispatcherTimer { Interval = ShiftInterval };
                _timer.Tick += Timer_Tick;
                _timer.Start();
            }
        }
    }

    private static void Timer_Tick(object? sender, EventArgs e)
    {
        // In a real implementation, this would iterate over attached controls.
        // For simplicity in Avalonia, applying it to the MainWindow or root containers
        // via a shared static state or finding them via VisualTree is preferred.
        // We will wire this cleanly once we see MainViewModel's idle tracking.
    }
}