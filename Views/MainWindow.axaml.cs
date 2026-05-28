using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using NullWave.ViewModels;

namespace NullWave.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnTrackSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.Library.SelectedTrack != null)
            vm.Detail.OpenFor(vm.Library.SelectedTrack);
    }
}