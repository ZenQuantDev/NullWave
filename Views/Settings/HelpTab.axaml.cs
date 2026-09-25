using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input.Platform;
using NullWave.Services;
using NullWave.ViewModels;
using NullWave.Models; 

namespace NullWave.Views.Settings;

public partial class HelpTab : UserControl
{
    public HelpTab()
    {
        InitializeComponent();
    }

    private async void OnCopyLogClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
            await CopyAsync(vm.LogViewer.FilteredLog);
    }

    private async void OnCopyDiagnosticsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
            await CopyAsync(vm.BuildDiagnosticsText());
    }

    private async Task CopyAsync(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard == null) return;
        await clipboard.SetTextAsync(text);
        ToastService.Instance.Show("Copied to clipboard.", ToastType.Success);
    }
}