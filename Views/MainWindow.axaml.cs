using System;
using System.Diagnostics;
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
        
        // Note: If OnKeyDown isn't already hooked up in your MainWindow.axaml file, 
        // you may need to add `KeyDown += OnKeyDown;` here.
    }

    private void OnMainWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        try
        {
            if (DataContext is MainViewModel vm)
            {
                // Stop the 30-second health check timer so it doesn't
                // fire after the window is gone
                vm.Settings.StopHealthCheck();

                // Unload the Ollama model from RAM/VRAM on exit.
                // The daemon keeps running (we can't kill a system service),
                // but "ollama stop <model>" evicts the weights from memory.
                var model = vm.Settings.SelectedModel;
                if (!string.IsNullOrWhiteSpace(model) &&
                    vm.Settings.AIServiceState == AIServiceState.Running)
                {
                    Log.Information("[MainWindow] Unloading Ollama model '{Model}' before exit", model);
                    Process.Start(new ProcessStartInfo
                    {
                        FileName               = "ollama",
                        Arguments              = $"stop {model}",
                        UseShellExecute        = false,
                        CreateNoWindow         = true
                    });
                }
            }
        }
        catch (Exception ex)
        {
            // Swallow — if ollama isn't on PATH or fails, the user still closes normally
            Log.Warning(ex, "[MainWindow] Could not unload Ollama model on exit");
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        // Alt — toggle menu bar
        if (e.Key == Key.LeftAlt || e.Key == Key.RightAlt)
        {
            vm.ToggleMenuBar();
            e.Handled = true;
            return;
        }

        // Don't fire shortcuts when typing in a TextBox
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