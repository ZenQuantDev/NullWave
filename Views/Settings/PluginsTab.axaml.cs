using Avalonia.Controls;
using Avalonia.Interactivity;
using NullWave.ViewModels.Settings;

namespace NullWave.Views.Settings;

public partial class PluginsTab : UserControl
{
    public PluginsTab()
    {
        InitializeComponent();
    }

    // Click fires only on real user interaction, never on binding updates.
    private void OnPluginToggleClick(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggle && toggle.DataContext is PluginRowViewModel row)
            row.UserToggled(toggle.IsChecked == true);
    }
}