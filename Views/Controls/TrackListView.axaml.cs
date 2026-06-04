using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using NullWave.ViewModels;

namespace NullWave.Views.Controls;

public partial class TrackListView : DockPanel
{
    public TrackListView()
    {
        InitializeComponent();
    }

    private void OnTrackSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.Library.SelectedTrack != null)
        {
            vm.Detail.OpenFor(vm.Library.SelectedTrack);
        }
    }

    // NEW: Double-click a track to play it immediately
    private void OnTrackDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.Library.SelectedTrack != null)
        {
            // Execute the existing command instead of trying to invoke the event directly
            vm.Library.PlayTrackCommand.Execute(vm.Library.SelectedTrack);
        }
    }
}