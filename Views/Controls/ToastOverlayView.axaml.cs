using Avalonia.Controls;
using Avalonia.Input;
using NullWave.Models;
using NullWave.Services;

namespace NullWave.Views.Controls;

public partial class ToastOverlayView : UserControl
{
    public ToastOverlayView()
    {
        InitializeComponent();
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