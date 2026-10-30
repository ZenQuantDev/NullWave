using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using NullWave.Helpers;
using NullWave.Models;
using NullWave.Services;
using NullWave.ViewModels;
using NullWave.ViewModels.Base;

namespace NullWave.Views.Controls;

public partial class PlaylistsView : DockPanel
{
    private Track? _draggedTrack;

    public PlaylistsView()
    {
        InitializeComponent();
    }

    // --- Lazy row menus (parity with TrackListView) ---

    private void OnListContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not ListBox list) return;
        var track = FindRowTrack(e.Source as Visual);
        if (track == null) return;
        e.Handled = true;
        BuildRowMenu(track).ShowAt(list, true);
    }

    private void OnRowMenuClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        var track = FindRowTrack(btn);
        if (track == null) return;
        BuildRowMenu(track).ShowAt(btn);
    }

    private static Track? FindRowTrack(Visual? source)
    {
        var v = source;
        while (v != null)
        {
            if (v.DataContext is Track t) return t;
            v = v.GetVisualParent();
        }
        return null;
    }

    private MenuFlyout BuildRowMenu(Track track)
    {
        var menu = new MenuFlyout();
        if (DataContext is not MainViewModel root) return menu;
        var lib = root.Library;
        var loc = LocalizationService.Instance;

        AddItem(menu, loc["ContextMenu_Play"], new RelayCommand(() => root.Player.PlayTrack(track)), null);
        AddItem(menu, loc["ContextMenu_ViewDetails"], new RelayCommand(() => root.Detail.OpenFor(track)), null);
        AddItem(menu, loc["ContextMenu_ToggleFavorite"], lib.ToggleFavoriteCommand, track);
        AddItem(menu, loc["ContextMenu_AddToQueue"], lib.AddToQueueCommand, track);
        AddItem(menu, loc["ContextMenu_AddToPlaylist"], root.AddTrackToPlaylistCommand, track);
        AddItem(menu, loc["ContextMenu_CopyUrl"], lib.CopyUrlCommand, null);

        if (root.Playlist.SelectedPlaylist != null)
        {
            menu.Items.Add(new Separator());
            AddItem(menu, loc["ContextMenu_Remove"], root.Playlist.RemoveFromPlaylistCommand, track);
        }

        return menu;
    }

    private static void AddItem(MenuFlyout menu, string header, ICommand command, object? parameter)
        => menu.Items.Add(new MenuItem { Header = header, Command = command, CommandParameter = parameter });

    private async void OnImportM3uClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var window = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow : null;
        if (window == null) return;
        var dialog = new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Select M3U Playlist",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new Avalonia.Platform.Storage.FilePickerFileType("M3U Playlist")
                {
                    Patterns = new[] { "*.m3u", "*.m3u8" }
                }
            }
        };
        var files = await window.StorageProvider.OpenFilePickerAsync(dialog);
        if (files.Count == 0) return;

        var m3uPath = files[0].Path.LocalPath;
        var importer = new M3uImporter(vm.LibraryService);
        var importVm = new ViewModels.Dialogs.M3uImportDialogViewModel(importer, m3uPath);
        var importDialog = new Views.Dialogs.M3uImportDialog { DataContext = importVm };
        importVm.CloseRequested += () => importDialog.Close();
        await importDialog.ShowDialog(window);
        // WIRE FIX: Pass the result to PlaylistViewModel to handle the DB insertion
        if (importVm.DialogResult != null)
        {
            await vm.Playlist.ImportM3uPlaylistAsync(importVm.DialogResult.Preview, importVm.DialogResult.PlaylistName);
        }
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
        if (track != null && DataContext is not null && DataContext is MainViewModel vm)
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