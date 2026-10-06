using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using NullWave.Helpers;
using NullWave.ViewModels;
using NullWave.Helpers.Diagnostics;
using Serilog;

namespace NullWave.Views;

public partial class MainWindow : Window
{
    // Hover tagging for the Perf log: which control class was under the cursor
    // during each measured second. Rebuilt only when the hovered target changes,
    // never per mouse-move, so the logger adds no GC pressure of its own.
    private string _lastHoverDescription = "none";
    private object? _lastHoverKey;

    public MainWindow()
    {
        InitializeComponent();
        WallpaperChromeSync.Attach(this);
        DataContext = new MainViewModel();
        Closing += OnMainWindowClosing;
        Opened += OnMainWindowOpened;

        // Subscribe to DevTools events once DataContext is ready
        if (DataContext is MainViewModel mvm)
        {
            mvm.Settings.HoverTaggingChanged += OnHoverTaggingChanged;
            mvm.Settings.TogglePerfOverlayRequested += OnTogglePerfOverlayRequested;
        }

        // MainWindow owns page/sidebar/hover context; the shared controller reuses
        // this tag provider no matter which window presses F3.
        PerfOverlayController.SetTagProvider(() =>
        {
            var mvm = DataContext as MainViewModel;
            var page = mvm?.CurrentPage ?? "unknown";
            var sidebar = mvm?.IsSidebarExpanded == true ? "expanded" : "rail";
            return "page=" + page + "|hover=" + _lastHoverDescription + "|sidebar=" + sidebar;
        });
    }

    private void OnHoverTaggingChanged(bool enabled)
    {
        if (enabled)
        {
            this.AddHandler(InputElement.PointerMovedEvent, OnGlobalPointerMoved,
                RoutingStrategies.Tunnel, handledEventsToo: true);
        }
        else
        {
            this.RemoveHandler(InputElement.PointerMovedEvent, OnGlobalPointerMoved);
        }
    }

    private void OnTogglePerfOverlayRequested() => PerfOverlayController.Toggle();

    private void OnGlobalPointerMoved(object? sender, PointerEventArgs e)
    {
        if (e.Source is not Visual start) return;

        Control? found = start as Control;
        if (found == null || found.Classes.Count == 0)
        {
            found = null;
            foreach (var ancestor in start.GetVisualAncestors())
            {
                if (ancestor is Control c && c.Classes.Count > 0) { found = c; break; }
            }
        }

        // Identity guard: skip all string work while the pointer stays on the same target.
        object key = found ?? (object)start.GetType();
        if (key.Equals(_lastHoverKey)) return;
        _lastHoverKey = key;

        // FILTER: pseudo-classes (":pointerover") live inside Classes in Avalonia and
        // were leaking into the perf tag, making logs harder to read.
        _lastHoverDescription = found != null
            ? $"{found.GetType().Name}.{string.Join('.', found.Classes.Where(c => !c.StartsWith(':')))}"
            : start.GetType().Name;
    }

    private async void OnMainWindowOpened(object? sender, EventArgs e)
    {
        Opened -= OnMainWindowOpened;

        // FIX: Force the visual tree to re-evaluate CurrentPage bindings.
        if (DataContext is MainViewModel vm)
        {
            var currentPage = vm.CurrentPage;
            vm.CurrentPage = string.Empty;
            vm.CurrentPage = currentPage;
        }

        if (DataContext is MainViewModel vm2 && vm2.ShouldShowOnboarding)
        {
            try
            {
                await new OnboardingWindow(vm2.Settings).ShowDialog(this);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[MainWindow] Onboarding wizard failed to show");
            }
        }
    }

    private void OnMainWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        try
        {
            if (DataContext is MainViewModel vm)
            {
                vm.Settings.StopHealthCheck();
                vm.DisposePowerState();

                try
                {
                    var unloadTask = vm.UnloadAIModelAsync();
                    if (!unloadTask.Wait(TimeSpan.FromMilliseconds(700)))
                    {
                        Log.Warning("[MainWindow] AI unload still in flight at exit; Ollama keep_alive policy will evict the model automatically.");
                    }
                    else
                    {
                        Log.Debug("[MainWindow] AI model unload sequence completed during shutdown.");
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "[MainWindow] Failed to unload AI model on exit");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[MainWindow] Could not unload Ollama model on exit");
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        if (e.Key == Key.L && (e.KeyModifiers & KeyModifiers.Control) != 0)
        {
            GlobalSearchBox.Focus();
            GlobalSearchBox.SelectAll();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.LeftAlt || e.Key == Key.RightAlt || (e.KeyModifiers & KeyModifiers.Alt) != 0)
        {
            if (e.Key == Key.LeftAlt || e.Key == Key.RightAlt || e.Key == Key.F10)
            {
                vm.ToggleMenuBar();
                e.Handled = true;
                return;
            }
        }
        if (e.Key == Key.B && (e.KeyModifiers & KeyModifiers.Control) != 0)
        {
            vm.ToggleSidebarCollapsedCommand.Execute(null);
            e.Handled = true;
            return;
        }
        if (IsWithinTextInput(e.Source)) return;
        switch (e.Key)
        {
            case Key.Space:
                vm.Player.PlayPauseCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Left:
                vm.Player.SeekBackwardCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Right:
                vm.Player.SeekForwardCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.M:
                vm.Player.ToggleMuteCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.N:
                vm.Player.NextTrackCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.P:
                vm.Player.PreviousTrackCommand.Execute(null);
                e.Handled = true;
                break;
        }
        if (e.Key == Key.F11 || (e.Key == Key.Return && (e.KeyModifiers & KeyModifiers.Alt) != 0))
        {
            ToggleFullscreen();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.F3)
        {
            OnTogglePerfOverlayRequested();
            e.Handled = true;
            return;
        }
    }

    private WindowState _lastNonFullscreenState = WindowState.Normal;

    private void ToggleFullscreen()
    {
        if (WindowState == WindowState.FullScreen)
        {
            WindowState = _lastNonFullscreenState;
            Log.Information("[MainWindow] Fullscreen exited -> {State}", WindowState);
            return;
        }

        if (WindowState == WindowState.Maximized)
            WindowState = WindowState.Normal;

        _lastNonFullscreenState = WindowState;
        WindowState = WindowState.FullScreen;
        Log.Information("[MainWindow] Fullscreen entered");
    }

    private static bool IsWithinTextInput(object? source)
    {
        if (source is not Visual visual) return false;
        foreach (var ancestor in visual.GetVisualAncestors())
        {
            if (ancestor is TextBox or AutoCompleteBox) return true;
        }
        return source is TextBox or AutoCompleteBox;
    }
}