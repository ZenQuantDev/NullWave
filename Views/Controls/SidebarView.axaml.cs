using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using NullWave.Models;
using NullWave.Services; 
using NullWave.ViewModels;

namespace NullWave.Views.Controls;

public partial class SidebarView : Border
{
    private static readonly DataFormat<NavItem> NavItemFormat =
        DataFormat.CreateInProcessFormat<NavItem>("nullwave-navitem");

    private static readonly DataFormat<Playlist> PlaylistFormat =
        DataFormat.CreateInProcessFormat<Playlist>("nullwave-playlist");

    private PointerPressedEventArgs? _pendingDragArgs;
    private Playlist? _pendingDragPlaylist;
    private Point _dragStart;

    public SidebarView()
    {
        InitializeComponent();
        // Buttons swallow PointerPressed, so listen with handledEventsToo to enable row dragging.
        this.AddHandler(InputElement.PointerPressedEvent, OnRowDragPointerPressed,
            RoutingStrategies.Bubble, handledEventsToo: true);
    }

    //  Row drag (unpinned playlists only; pinned stay locked) 
    private void OnRowDragPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _pendingDragArgs = null;
        _pendingDragPlaylist = null;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.Source is not Visual source) return;

        var row = FindPlaylistRow(source);
        if (row == null) return; // pinned rows / folders carry non-Playlist tags → not draggable

        _pendingDragArgs = e;
        _pendingDragPlaylist = row.Tag as Playlist;
        _dragStart = e.GetPosition(this);
    }

    private async void OnRowDragPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pendingDragArgs == null || _pendingDragPlaylist == null) return;

        var delta = e.GetPosition(this) - _dragStart;
        if (delta.X * delta.X + delta.Y * delta.Y < 64) return; // 8px threshold → clicks still work

        var args = _pendingDragArgs;
        var playlist = _pendingDragPlaylist;
        _pendingDragArgs = null;
        _pendingDragPlaylist = null;

        var data = new DataTransfer();
        var item = new DataTransferItem();
        item.Set(PlaylistFormat, playlist);
        data.Add(item);
        await DragDrop.DoDragDropAsync(args, data, DragDropEffects.Move);
    }

    private void OnRowDragPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _pendingDragArgs = null;
        _pendingDragPlaylist = null;
    }

    private static Border? FindPlaylistRow(Visual source)
    {
        Visual? v = source;
        while (v != null)
        {
            if (v is Border b && b.Classes.Contains("playlist-row") && b.Tag is Playlist)
                return b;
            v = v.GetVisualParent();
        }
        return null;
    }

    //  Folder drop targets 
    private void OnFolderDragEnter(object? sender, DragEventArgs e)
    {
        if (sender is Border b) b.Classes.Add("drop-target");
    }

    private void OnFolderDragLeave(object? sender, DragEventArgs e)
    {
        if (sender is Border b) b.Classes.Remove("drop-target");
    }

    private void OnFolderDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (sender is not Border b || b.Tag is not SidebarFolderNode node) return;
        b.Classes.Remove("drop-target");

        if (e.DataTransfer.TryGetValue(PlaylistFormat) is { } pl)
            vm.Nav.MovePlaylistToFolder(pl.Id, node.Folder.Id);
        else if (e.DataTransfer.TryGetValue(NavItemFormat) is { } nav && nav.Playlist != null)
            vm.Nav.MovePlaylistToFolder(nav.Playlist.Id, node.Folder.Id);
    }

    //  Top-level drop zone (drag out of folders) 
    private void OnTopLevelDragEnter(object? sender, DragEventArgs e)
    {
        if (sender is Border b) b.Classes.Add("drop-target");
    }

    private void OnTopLevelDragLeave(object? sender, DragEventArgs e)
    {
        if (sender is Border b) b.Classes.Remove("drop-target");
    }

    private void OnTopLevelDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (sender is not Border b) return;
        b.Classes.Remove("drop-target");
        if (e.DataTransfer.TryGetValue(PlaylistFormat) is { } pl)
            vm.Nav.MovePlaylistToFolder(pl.Id, null);
    }

    //  Pinned reorder (customize mode only) 
    private async void OnNavItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || !vm.IsCustomizingSidebar) return;
        if (sender is not Border handle || handle.Tag is not NavItem item) return;
        if (!e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed) return;

        var dataItem = new DataTransferItem();
        dataItem.Set(NavItemFormat, item);
        var data = new DataTransfer();
        data.Add(dataItem);

        item.IsDragging = true;
        try
        {
            await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move);
        }
        finally
        {
            item.IsDragging = false;
            foreach (var navItem in vm.Nav.Items) navItem.IsDropTarget = false;
        }
    }

    private void OnNavItemDragEnter(object? sender, DragEventArgs e)
    {
        if (sender is Border targetBorder && targetBorder.Tag is NavItem targetItem)
            targetItem.IsDropTarget = true;
    }

    private void OnNavItemDragLeave(object? sender, DragEventArgs e)
    {
        if (sender is Border targetBorder && targetBorder.Tag is NavItem targetItem)
            targetItem.IsDropTarget = false;
    }

    private void OnNavItemDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (sender is not Border targetBorder || targetBorder.Tag is not NavItem targetItem) return;

        targetItem.IsDropTarget = false;
        var draggedItem = e.DataTransfer.TryGetValue(NavItemFormat);
        if (draggedItem is null || draggedItem == targetItem) return;

        var newIndex = vm.Nav.Items.IndexOf(targetItem);
        vm.Nav.MoveItem(draggedItem, newIndex);
    }

    //  Double-tap to play 
    private void OnPlaylistRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Border b && b.Tag is Playlist pl && DataContext is MainViewModel vm)
            vm.PlayPlaylistCommand.Execute(pl);
    }

    private void OnNavItemDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Border b && b.Tag is NavItem item && item.Playlist != null && DataContext is MainViewModel vm)
            vm.PlayPlaylistCommand.Execute(item.Playlist);
    }

    //  Context menus 
    private Playlist? MenuPlaylist(object? sender) =>
        sender is MenuItem mi && mi.Parent is ContextMenu cm && cm.PlacementTarget is Border b && b.Tag is Playlist pl ? pl : null;

    private SidebarFolderNode? MenuFolder(object? sender) =>
        sender is MenuItem mi && mi.Parent is ContextMenu cm && cm.PlacementTarget is Border b && b.Tag is SidebarFolderNode n ? n : null;

    private void OnPlaylistMenuPlay(object? s, RoutedEventArgs e)
    { if (MenuPlaylist(s) is { } pl && DataContext is MainViewModel vm) vm.PlayPlaylistCommand.Execute(pl); }

    private void OnPlaylistMenuQueue(object? s, RoutedEventArgs e)
    {
        if (MenuPlaylist(s) is not { } pl || DataContext is not MainViewModel vm) return;
        foreach (var t in pl.Tracks) vm.Library.AddToQueueCommand.Execute(t);
        ToastService.Instance.Show($"Added {pl.Tracks.Count} track(s) to queue.", ToastType.Success, scope: "queue-add");
    }

    private void OnPlaylistMenuMoveToFolder(object? s, RoutedEventArgs e)
    { if (MenuPlaylist(s) is { } pl && DataContext is MainViewModel vm) vm.MovePlaylistToFolderCommand.Execute(pl); }

    private void OnPlaylistMenuPin(object? s, RoutedEventArgs e)
    { if (MenuPlaylist(s) is { } pl && DataContext is MainViewModel vm) vm.Nav.PinPlaylist(pl.Id, pl.Name); }

    private void OnFolderMenuRename(object? s, RoutedEventArgs e)
    { if (MenuFolder(s) is { } n && DataContext is MainViewModel vm) vm.Nav.RenameFolderCommand.Execute(n); }

    private void OnFolderMenuDelete(object? s, RoutedEventArgs e)
    { if (MenuFolder(s) is { } n && DataContext is MainViewModel vm) vm.Nav.DeleteFolderCommand.Execute(n); }
}