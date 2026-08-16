using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using NullWave.ViewModels;
using NullWave.Models;
using NullWave.Services;
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
                vm.DisposePowerState();
                var model = vm.Settings.SelectedModel;
                if (!string.IsNullOrWhiteSpace(model) && vm.Settings.AiServiceState == AIServiceState.Running)
                {
                    Log.Information("[MainWindow] Offloading Ollama model '{Model}' to background worker process for exit sequence", model);
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

    //  Toast hover-pause handlers 
    private void OnToastPointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Control c && c.DataContext is LiveNotification n)
            ToastService.Instance.PauseAutoDismiss(n);
    }

    private void OnToastPointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is Control c && c.DataContext is LiveNotification n)
            ToastService.Instance.ResumeAutoDismiss(n);
    }
}