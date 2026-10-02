using Avalonia.Controls;
using NullWave.ViewModels;

namespace NullWave.Views.Settings;

public partial class DevTab : UserControl
{
    public DevTab()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => TryInitialScan();
    }

    private void TryInitialScan()
    {
        if (DataContext is SettingsViewModel vm && vm.IsDevMode && vm.DevBinaries.Count == 0)
            vm.DevRefreshBinariesCommand.Execute(null);
    }
}