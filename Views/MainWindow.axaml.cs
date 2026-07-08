using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
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
    }

    private void OnMainWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        try
        {
            if (DataContext is MainViewModel vm)
            {
                vm.Settings.StopHealthCheck();
                vm.DisposePowerState(); // Clean up native polling allocations

                var model = vm.Settings.SelectedModel;
                if (!string.IsNullOrWhiteSpace(model) && vm.Settings.AiServiceState == AIServiceState.Running)
                {
                    Log.Information("[MainWindow] Offloading Ollama model '{Model}' to background worker process for exit sequence", model);
                    
                    // Run the process detached on a threadpool task worker so the UI shuts down instantly
                    Task.Run(() =>
                    {
                        try
                        {
                            var startInfo = new ProcessStartInfo
                            {
                                FileName = "ollama",
                                Arguments = $"stop {model}",
                                UseShellExecute = false,
                                CreateNoWindow = true
                            };

                            using var ollamaProcess = Process.Start(startInfo);
                            ollamaProcess?.WaitForExit(2000);
                            Log.Information("[MainWindow] Asynchronous VRAM flush call completed for '{Model}'.", model);
                        }
                        catch (Exception ex)
                        {
                            Log.Debug(ex, "[MainWindow] Background VRAM flush task encountered errors.");
                        }
                    });
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

        // Clean hotkey implementation for tracking Alt modifiers
        if (e.Key == Key.LeftAlt || e.Key == Key.RightAlt || (e.KeyModifiers & KeyModifiers.Alt) != 0)
        {
            // Only toggle if Alt is hit cleanly standalone or F10 fallback is used
            if (e.Key == Key.LeftAlt || e.Key == Key.RightAlt || e.Key == Key.F10)
            {
                vm.ToggleMenuBar();
                e.Handled = true;
                return;
            }
        }

        // Do not intercept hotkeys if typing inside data inputs or search boxes
        if (e.Source is TextBox) return;

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
    }
}