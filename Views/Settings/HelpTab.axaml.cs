using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input.Platform;
using NullWave.Services;
using NullWave.ViewModels;
using NullWave.Models; 
using NullWave.Helpers.Diagnostics;

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
            // Redact paths from the filtered log view
            await CopyAsync(DiagnosticsRedactor.RedactText(vm.LogViewer.FilteredLog));
    }

    private async void OnCopyDiagnosticsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
            // Redact paths from the full diagnostics dump
            await CopyAsync(DiagnosticsRedactor.RedactText(vm.BuildDiagnosticsText()));
    }

    private async Task CopyAsync(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard == null) return;
        await clipboard.SetTextAsync(text);
        ToastService.Instance.Show(LocalizationService.Instance["Help_CopiedRedacted"], ToastType.Success);
    }
}