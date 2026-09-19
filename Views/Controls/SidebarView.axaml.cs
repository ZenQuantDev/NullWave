using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using NullWave.Models;
using NullWave.Services;
using NullWave.ViewModels;
using Serilog;

namespace NullWave.Views.Controls;

public partial class SidebarView : Border
{
    private static readonly DataFormat<Playlist> PlaylistFormat =
        DataFormat.CreateInProcessFormat<Playlist>("nullwave-playlist");
    private static readonly DataFormat<NavItem> NavItemFormat =
        DataFormat.CreateInProcessFormat<NavItem>("nullwave-navitem");

    private PointerPressedEventArgs? _pendingDragArgs;
    private Playlist? _pendingDragPlaylist;
    private NavItem? _pendingDragNavItem;
    private Point _dragStart;

    public SidebarView()
    {
        InitializeComponent();
        // handledEventsToo: inner Buttons swallow PointerPressed; we must still see it.
        this.AddHandler(InputElement.PointerPressedEvent, OnRowDragPointerPressed,
            RoutingStrategies.Bubble, handledEventsToo: true);
        this.AddHandler(InputElement.PointerCaptureLostEvent, OnPointerCaptureLost,
            RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _pendingDragArgs = null;
        _pendingDragPlaylist = null;
        _pendingDragNavItem = null;
        if (DataContext is MainViewModel vm) vm.Nav.IsReorderDragging = false;
    }

    // Unified drag start: playlist rows always draggable; pinned NavItem rows only in customize mode.
    public void OnRowDragPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _pendingDragArgs = null;
        _pendingDragPlaylist = null;
        _pendingDragNavItem = null;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.Source is not Visual source) return;

        // Playlist rows (unpinned list + folder children)
        var playlistRow = FindPlaylistRow(source);
        if (playlistRow != null)
        {
            _pendingDragArgs = e;
            _pendingDragPlaylist = playlistRow.Tag as Playlist;
            _dragStart = e.GetPosition(this);
            return;
        }

        // Nav rows (core + pinned) - only while customize mode is active.
        // Must pass through handledEventsToo handler because inner Buttons swallow PointerPressed.
        var navRow = FindNavItemRow(source);
        if (navRow != null && DataContext is MainViewModel vm && vm.IsCustomizingSidebar)
        {
            _pendingDragArgs = e;
            _pendingDragNavItem = navRow.Tag as NavItem;
            _dragStart = e.GetPosition(this);
        }
    }

    // Forwarding alias so XAML bindings referencing OnNavItemPointerPressed compile cleanly
    public void OnNavItemPointerPressed(object? sender, PointerPressedEventArgs e)
        => OnRowDragPointerPressed(sender, e);

    public async void OnRowDragPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pendingDragArgs == null || (_pendingDragPlaylist == null && _pendingDragNavItem == null)) return;

        var delta = e.GetPosition(this) - _dragStart;
        if (delta.X * delta.X + delta.Y * delta.Y < 64) return; // 8px drag threshold

        var args = _pendingDragArgs;
        var playlist = _pendingDragPlaylist;
        var navItem = _pendingDragNavItem;
        _pendingDragArgs = null;
        _pendingDragPlaylist = null;
        _pendingDragNavItem = null;

        var item = new DataTransferItem();
        if (playlist != null)
        {
            item.Set(PlaylistFormat, playlist);
        }
        else if (navItem != null)
        {
            item.Set(NavItemFormat, navItem);
        }
        else
        {
            return;
        }

        var data = new DataTransfer();
        data.Add(item);

        if (navItem != null && DataContext is MainViewModel vmStart)
        {
            navItem.IsDragging = true;
            vmStart.Nav.IsReorderDragging = true;
        }

        try
        {
            await DragDrop.DoDragDropAsync(args, data, DragDropEffects.Move);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[SidebarView] Sidebar drag operation failed");
        }
        finally
        {
            if (navItem != null && DataContext is MainViewModel vmEnd)
            {
                navItem.IsDragging = false;
                vmEnd.Nav.IsReorderDragging = false;
                foreach (var n in vmEnd.Nav.Items) n.IsDropTarget = false;
            }
        }
    }

    public void OnRowDragPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _pendingDragArgs = null;
        _pendingDragPlaylist = null;
        _pendingDragNavItem = null;
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

    private static Border? FindNavItemRow(Visual source)
    {
        Visual? v = source;
        while (v != null)
        {
            if (v is Border b && b.Classes.Contains("playlist-row") && b.Tag is NavItem)
                return b;
            v = v.GetVisualParent();
        }
        return null;
    }

    // Folder drop targets
    public void OnFolderDragEnter(object? sender, DragEventArgs e)
    {
        if (sender is Border b) b.Classes.Add("drop-target");
    }

    public void OnFolderDragLeave(object? sender, DragEventArgs e)
    {
        if (sender is Border b) b.Classes.Remove("drop-target");
    }

    public void OnFolderDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (sender is not Border b || b.Tag is not SidebarFolderNode node) return;
        b.Classes.Remove("drop-target");

        if (e.DataTransfer.TryGetValue(PlaylistFormat) is { } pl)
            vm.Nav.MovePlaylistToFolder(pl.Id, node.Folder.Id);
        else if (e.DataTransfer.TryGetValue(NavItemFormat) is { } nav && nav.Playlist != null)
            vm.Nav.MovePlaylistToFolder(nav.Playlist.Id, node.Folder.Id);
    }

    // Top-level drop zone (unpin target)
    public void OnTopLevelDragEnter(object? sender, DragEventArgs e)
    {
        if (sender is Border b) b.Classes.Add("drop-target");
    }

    public void OnTopLevelDragLeave(object? sender, DragEventArgs e)
    {
        if (sender is Border b) b.Classes.Remove("drop-target");
    }

    public void OnTopLevelDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (sender is not Border b) return;
        b.Classes.Remove("drop-target");

        if (e.DataTransfer.TryGetValue(PlaylistFormat) is { } pl)
            vm.Nav.MovePlaylistToFolder(pl.Id, null);
        else if (e.DataTransfer.TryGetValue(NavItemFormat) is { } nav && nav.Playlist != null)
            vm.Nav.UnpinPlaylist(nav.Playlist.Id);
    }

    // Pinned reorder drop targets (customize mode)
    public void OnNavItemDragEnter(object? sender, DragEventArgs e)
    {
        if (sender is Border targetBorder && targetBorder.Tag is NavItem targetItem)
            targetItem.IsDropTarget = true;
    }

    public void OnNavItemDragLeave(object? sender, DragEventArgs e)
    {
        if (sender is Border targetBorder && targetBorder.Tag is NavItem targetItem)
            targetItem.IsDropTarget = false;
    }

    public void OnNavItemDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (sender is not Border targetBorder || targetBorder.Tag is not NavItem targetItem) return;

        targetItem.IsDropTarget = false;
        var draggedItem = e.DataTransfer.TryGetValue(NavItemFormat);
        if (draggedItem is null) return;

        var draggedKey = NavigationViewModel.NavKey(draggedItem);
        var targetKey = NavigationViewModel.NavKey(targetItem);
        
        var actualDragged = vm.Nav.Items.FirstOrDefault(i => NavigationViewModel.NavKey(i) == draggedKey);
        var actualTarget = vm.Nav.Items.FirstOrDefault(i => NavigationViewModel.NavKey(i) == targetKey);
        
        if (actualDragged == null || actualTarget == null || actualDragged == actualTarget) return;

        var newIndex = vm.Nav.Items.IndexOf(actualTarget);
        vm.Nav.MoveItem(actualDragged, newIndex);
    }

    // Double-tap handlers
    public void OnPlaylistRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Border b && b.Tag is Playlist pl && DataContext is MainViewModel vm)
            vm.PlayPlaylistCommand.Execute(pl);
    }

    public void OnNavItemDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Border b && b.Tag is NavItem item && item.Playlist != null && DataContext is MainViewModel vm)
            vm.PlayPlaylistCommand.Execute(item.Playlist);
    }

    // Context menus
    private Playlist? MenuPlaylist(object? sender) =>
        sender is MenuItem mi && mi.Parent is ContextMenu cm && cm.PlacementTarget is Border b && b.Tag is Playlist pl ? pl : null;

    private SidebarFolderNode? MenuFolder(object? sender) =>
        sender is MenuItem mi && mi.Parent is ContextMenu cm && cm.PlacementTarget is Border b && b.Tag is SidebarFolderNode n ? n : null;

    private NavItem? MenuNavItem(object? sender) =>
        sender is MenuItem mi && mi.Parent is ContextMenu cm && cm.PlacementTarget is Border b && b.Tag is NavItem n ? n : null;

    public void OnPlaylistMenuPlay(object? s, RoutedEventArgs e)
    { if (MenuPlaylist(s) is { } pl && DataContext is MainViewModel vm) vm.PlayPlaylistCommand.Execute(pl); }

    public void OnPlaylistMenuQueue(object? s, RoutedEventArgs e)
    {
        if (MenuPlaylist(s) is not { } pl || DataContext is not MainViewModel vm) return;
        foreach (var t in pl.Tracks) vm.Library.AddToQueueCommand.Execute(t);
        ToastService.Instance.Show($"Added {pl.Tracks.Count} track(s) to queue.", ToastType.Success, scope: "queue-add");
    }

    public void OnPlaylistMenuMoveToFolder(object? s, RoutedEventArgs e)
    { if (MenuPlaylist(s) is { } pl && DataContext is MainViewModel vm) vm.MovePlaylistToFolderCommand.Execute(pl); }

    public void OnPlaylistMenuPin(object? s, RoutedEventArgs e)
    { if (MenuPlaylist(s) is { } pl && DataContext is MainViewModel vm) vm.Nav.PinPlaylist(pl.Id, pl.Name); }

    public void OnFolderMenuRename(object? s, RoutedEventArgs e)
    { if (MenuFolder(s) is { } n && DataContext is MainViewModel vm) vm.Nav.RenameFolderCommand.Execute(n); }

    public void OnFolderMenuDelete(object? s, RoutedEventArgs e)
    { if (MenuFolder(s) is { } n && DataContext is MainViewModel vm) vm.Nav.DeleteFolderCommand.Execute(n); }

    public void OnNavItemMenuPlay(object? s, RoutedEventArgs e)
    {
        if (MenuNavItem(s) is { Playlist: { } pl } && DataContext is MainViewModel vm)
            vm.PlayPlaylistCommand.Execute(pl);
    }

    public void OnNavItemMenuQueue(object? s, RoutedEventArgs e)
    {
        if (MenuNavItem(s) is not { Playlist: { } pl } || DataContext is not MainViewModel vm) return;
        foreach (var t in pl.Tracks) vm.Library.AddToQueueCommand.Execute(t);
        ToastService.Instance.Show($"Added {pl.Tracks.Count} track(s) to queue.", ToastType.Success, scope: "queue-add");
    }

    public void OnNavItemMenuUnpin(object? s, RoutedEventArgs e)
    {
        if (MenuNavItem(s) is { } n && DataContext is MainViewModel vm)
            vm.Nav.UnpinCommand.Execute(n);
    }
}