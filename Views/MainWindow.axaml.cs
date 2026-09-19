using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using NullWave.ViewModels;
using Serilog;

namespace NullWave.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
        Closing += OnMainWindowClosing;
        Opened += OnMainWindowOpened;
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

        // FIX: Ctrl+L to focus global search (placed before Alt check and text input guard)
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
        // F11 or Alt+Enter: toggle true fullscreen
        if (e.Key == Key.F11 || (e.Key == Key.Return && (e.KeyModifiers & KeyModifiers.Alt) != 0))
        {
            ToggleFullscreen();
            e.Handled = true;
            return;
        }
    }

    private WindowState _lastNonFullscreenState = WindowState.Normal;

    private void ToggleFullscreen()
    {
        if (WindowState == WindowState.FullScreen)
        {
            WindowState = _lastNonFullscreenState; // restores Normal or Maximized exactly
            Log.Information("[MainWindow] Fullscreen exited -> {State}", WindowState);
            return;
        }

        // Avalonia quirk (#7202): Maximized -> FullScreen yields a borderless Normal window.
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