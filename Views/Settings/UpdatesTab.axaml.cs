using Avalonia.Controls;
using NullWave.ViewModels;

namespace NullWave.Views.Settings;

public partial class UpdatesTab : UserControl
{
    private bool _scanned;

    public UpdatesTab()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) =>
        {
            if (_scanned || DataContext is not SettingsViewModel vm) return;
            _scanned = true;
            vm.CheckDependenciesCommand.Execute(null);
        };
    }
}
