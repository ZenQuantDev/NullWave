using Avalonia.Controls;
using Avalonia.Input;
using NullWave.Models;
using NullWave.ViewModels;

namespace NullWave.Views.Controls;

public partial class QueueView : Border
{
    public QueueView()
    {
        InitializeComponent();
    }

    private void OnTrackDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainViewModel vm && sender is Grid grid && grid.DataContext is Track track)
        {
            vm.Queue.PlayNowCommand.Execute(track);
        }
    }
}