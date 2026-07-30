// Views/Controls/QueueView.axaml.cs
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using NullWave.Models;
using NullWave.ViewModels;

namespace NullWave.Views.Controls;

public partial class QueueView : Border
{
    private static readonly DataFormat<Track> QueueTrackFormat =
        DataFormat.CreateInProcessFormat<Track>("nullwave-queue-track");

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

    private async void OnQueueItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainViewModel) return;
        if (sender is not Border handle || handle.Tag is not Track track) return;
        if (!e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed) return;

        var dataItem = new DataTransferItem();
        dataItem.Set(QueueTrackFormat, track);

        var data = new DataTransfer();
        data.Add(dataItem);

        await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move);
    }

    private void OnQueueItemDragEnter(object? sender, DragEventArgs e)
    {
        if (sender is Grid targetGrid && !targetGrid.Classes.Contains("drop-target"))
            targetGrid.Classes.Add("drop-target");
    }

    private void OnQueueItemDragLeave(object? sender, DragEventArgs e)
    {
        if (sender is Grid targetGrid)
            targetGrid.Classes.Remove("drop-target");
    }

    private void OnQueueItemDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (sender is not Grid targetGrid) return;

        targetGrid.Classes.Remove("drop-target");

        if (targetGrid.Tag is not Track targetTrack) return;
        var draggedTrack = e.DataTransfer.TryGetValue(QueueTrackFormat);
        if (draggedTrack is null || draggedTrack.Id == targetTrack.Id) return;

        var newIndex = vm.Queue.Tracks.ToList().FindIndex(t => t.Id == targetTrack.Id);
        vm.Queue.MoveTrackTo(draggedTrack, newIndex);
    }
}