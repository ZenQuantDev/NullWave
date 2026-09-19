using Avalonia.Controls;
using NullWave.Services;

namespace NullWave.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();

        // CRITICAL: single-host toast routing - while Settings is open,
        // toasts render here ONLY (MainWindow overlay hides itself).
        Opened += (_, _) => ToastService.Instance.SetActiveHost(true);
        Closed += (_, _) => ToastService.Instance.SetActiveHost(false);
    }
}