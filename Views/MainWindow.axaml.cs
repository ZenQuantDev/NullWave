// MainWindow.axaml.cs
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
    }

    private void OnMainWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        try
        {
            if (DataContext is MainViewModel vm)
            {
                vm.Settings.StopHealthCheck();

                var model = vm.Settings.SelectedModel;
                if (!string.IsNullOrWhiteSpace(model) && vm.Settings.AiServiceState == AIServiceState.Running)
                {
                    Log.Information("[MainWindow] Unloading Ollama model '{Model}' before exit", model);
                    
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "ollama",
                        Arguments = $"stop {model}",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using (var ollamaProcess = Process.Start(startInfo))
                    {
                        if (ollamaProcess != null)
                        {
                            bool exitedCleanly = ollamaProcess.WaitForExit(TimeSpan.FromSeconds(2));
                            if (exitedCleanly)
                            {
                                Log.Information("[MainWindow] Successfully unloaded model '{Model}' from VRAM.", model);
                            }
                            else
                            {
                                Log.Warning("[MainWindow] Ollama stop command timed out before application exit sequence.");
                            }
                        }
                    }
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

        if (e.Key == Key.LeftAlt || e.Key == Key.RightAlt)
        {
            vm.ToggleMenuBar();
            e.Handled = true;
            return;
        }

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