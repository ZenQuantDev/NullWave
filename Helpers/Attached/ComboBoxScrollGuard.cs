using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace NullWave.Helpers.Attached;

/// <summary>
/// Closes open ComboBox dropdowns when the user scrolls outside them.
/// Avalonia's Popup doesn't auto-close on wheel events, causing orphaned dropdowns
/// that drift away from their owner controls when the page scrolls.
/// </summary>
public static class ComboBoxScrollGuard
{
    private static readonly HashSet<ComboBox> Open = new();

    // Custom IObserver<T> because Avalonia 11+ exposes ComboBox.IsDropDownOpenProperty.Changed
    // as a plain IObservable<AvaloniaPropertyChangedEventArgs<bool>>, which has no
    // AddClassHandler extension and no Subscribe(Action<T>) overload without System.Reactive.
    private sealed class DropDownObserver : IObserver<AvaloniaPropertyChangedEventArgs<bool>>
    {
        public void OnCompleted() { }
        public void OnError(Exception error) { }

        public void OnNext(AvaloniaPropertyChangedEventArgs<bool> e)
        {
            if (e.Sender is ComboBox cb)
            {
                if (e.NewValue.GetValueOrDefault()) Open.Add(cb);
                else Open.Remove(cb);
            }
        }
    }

    public static void Register()
    {
        ComboBox.IsDropDownOpenProperty.Changed.Subscribe(new DropDownObserver());

        InputElement.PointerWheelChangedEvent.AddClassHandler<TopLevel>((_, e) =>
        {
            if (Open.Count == 0) return;

            if (e.Source is Visual v &&
                v.GetSelfAndVisualAncestors().OfType<Border>().Any(b => b.Name == "PopupBorder"))
                return;

            foreach (var cb in Open.ToArray())
                cb.IsDropDownOpen = false;
        }, RoutingStrategies.Tunnel);

        ScrollViewer.ScrollChangedEvent.AddClassHandler<TopLevel>((_, e) =>
        {
            if (Open.Count == 0) return;
            if (e.OffsetDelta != default)
            {
                if (e.Source is Visual v &&
                    v.GetSelfAndVisualAncestors().OfType<Border>().Any(b => b.Name == "PopupBorder"))
                    return;

                foreach (var cb in Open.ToArray())
                    cb.IsDropDownOpen = false;
            }
        });
    }
}