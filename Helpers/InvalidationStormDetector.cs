using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Serilog;

namespace NullWave.Helpers.Diagnostics;

/// <summary>
/// Attaches to a control and logs a warning if it undergoes excessive layout/render 
/// passes while the pointer is hovering over it. Helps identify "animation storms" 
/// or overlapping hit-test boundaries that waste GPU cycles.
/// </summary>
public class InvalidationStormDetector
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<InvalidationStormDetector, Control, bool>("IsEnabled");

    public static void SetIsEnabled(Control element, bool value) => element.SetValue(IsEnabledProperty, value);
    public static bool GetIsEnabled(Control element) => element.GetValue(IsEnabledProperty);

    static InvalidationStormDetector()
    {
        IsEnabledProperty.Changed.AddClassHandler<Control>(OnIsEnabledChanged);
    }

    private static void OnIsEnabledChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            var state = new ControlState();
            
            // Track when mouse enters/leaves the control boundaries
            control.AddHandler(InputElement.PointerEnteredEvent, (s, ev) => 
            { 
                state.IsPointerOver = true; 
                state.PassCount = 0; 
            }, RoutingStrategies.Tunnel);
            
            control.AddHandler(InputElement.PointerExitedEvent, (s, ev) => 
            { 
                state.IsPointerOver = false; 
                if (state.PassCount > 5) 
                    Log.Warning("[Perf] Invalidation storm on {Control}: {Count} layout/render passes during a single hover interaction.", control.GetType().Name, state.PassCount);
                state.PassCount = 0; 
            }, RoutingStrategies.Tunnel);
            
            // Count every layout/render pass that occurs while hovering
            control.LayoutUpdated += (s, ev) => 
            { 
                if (state.IsPointerOver) state.PassCount++; 
            };
        }
    }

    private class ControlState
    {
        public bool IsPointerOver;
        public int PassCount;
    }
}