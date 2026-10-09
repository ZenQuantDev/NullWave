using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using NullWave.ViewModels;
using NullWave.Models;

namespace NullWave.Views.Controls;

public partial class PlaylistsView : DockPanel
{
    private Track? _draggedTrack;

    public PlaylistsView()
    {
        InitializeComponent();
    }

    private void OnTrackSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox list) return;
        if (list.SelectedItems?.Count != 1) return;
        if (list.SelectedItem is not Track track) return;
        if (DataContext is not MainViewModel vm) return;
        vm.Playlist.OpenTrackDetail(track);
    }

    private void OnTrackDoubleTapped(object? sender, TappedEventArgs e)
    {
        var track = (e.Source as Control)?.DataContext as Track
                    ?? (sender as ListBox)?.SelectedItem as Track;
        if (track != null && DataContext is MainViewModel vm)
            vm.Player.PlayTrack(track);
    }

    private async void OnDragHandlePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control control) return;
        if (control.DataContext is not Track track) return;

        _draggedTrack = track;
        var dragData = new DataTransfer();
        dragData.Add(DataTransferItem.Create(DataFormat.Text, track.Id.ToString()));

        await DragDrop.DoDragDropAsync(e, dragData, DragDropEffects.Move);
    }

    private void OnRowDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.Text) ? DragDropEffects.Move : DragDropEffects.None;
    }

    private void OnRowDrop(object? sender, DragEventArgs e)
    {
        if (_draggedTrack == null) return;
        if (sender is not Control targetControl) return;
        if (targetControl.DataContext is not Track targetTrack) return;
        if (DataContext is not MainViewModel vm || vm.Playlist.SelectedPlaylist == null) return;

        var tracks = vm.Playlist.SelectedPlaylist.Tracks;
        var fromIndex = tracks.IndexOf(_draggedTrack);
        var toIndex = tracks.IndexOf(targetTrack);

        if (fromIndex < 0 || toIndex < 0 || fromIndex == toIndex) return;

        vm.Playlist.MoveTrackInSelectedPlaylist(fromIndex, toIndex);
        _draggedTrack = null;
    }
}