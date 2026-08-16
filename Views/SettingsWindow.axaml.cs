using Avalonia.Controls;
using Avalonia.Input;
using NullWave.Models;
using NullWave.Services;

namespace NullWave.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();

        // CRITICAL: single-host toast routing — while Settings is open,
        // toasts render here ONLY (MainWindow overlay hides itself).
        Opened += (_, _) => ToastService.Instance.SetActiveHost(true);
        Closed += (_, _) => ToastService.Instance.SetActiveHost(false);
    }

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