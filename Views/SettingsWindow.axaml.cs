using Avalonia.Controls;
using Avalonia.Input;
using NullWave.Helpers;
using NullWave.Helpers.Diagnostics;
using NullWave.Services;

namespace NullWave.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        WallpaperChromeSync.Attach(this);

        // CRITICAL: single-host toast routing - while Settings is open,
        // toasts render here ONLY (MainWindow overlay hides itself).
        Opened += (_, _) => ToastService.Instance.SetActiveHost(true);
        Closed += (_, _) => ToastService.Instance.SetActiveHost(false);

        // F3 debugging works inside Settings too (modal dialog steals focus,
        // so MainWindow's KeyDown never sees it).
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.F3)
            {
                PerfOverlayController.Toggle();
                e.Handled = true;
            }
        };
    }
}