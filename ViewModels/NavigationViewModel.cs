using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Material.Icons;
using NullWave.Helpers;
using NullWave.Models;
using NullWave.Services;
using NullWave.ViewModels.Base;

namespace NullWave.ViewModels;

public enum SidebarPill { Playlists, Artists }

public class NavigationViewModel : ViewModelBase
{
    private readonly PreferencesService _prefs;
    private readonly PlaylistService _playlists;
    private readonly Action<Guid> _navigateToPlaylist;
    private readonly List<NavItem> _coreItems;
    private SidebarPill _currentPill = SidebarPill.Playlists;

    public SidebarPill CurrentPill
    {
        get => _currentPill;
        set { _currentPill = value; OnPropertyChanged(); }
    }

    public ObservableCollection<NavItem> Items { get; } = new();
    public ObservableCollection<Playlist> UnpinnedPlaylists { get; } = new();
    public ObservableCollection<SidebarFolderNode> FolderNodes { get; } = new();

    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }
    public ICommand UnpinCommand { get; }
    public ICommand SelectPillCommand { get; }
    public ICommand ToggleFolderCommand { get; }
    public ICommand PinPlaylistCommand { get; }
    public ICommand RenameFolderCommand { get; }
    public ICommand DeleteFolderCommand { get; }

    public NavigationViewModel(
        PreferencesService prefs,
        PlaylistService playlists,
        ICommand navigateLibrary,
        ICommand navigatePlaylists,
        Action<Guid> navigateToPlaylist)
    {
        _prefs = prefs;
        _playlists = playlists;
        _navigateToPlaylist = navigateToPlaylist;

        _coreItems = new List<NavItem>
        {
            new("Library", "Library", MaterialIconKind.Bookshelf, NavItemType.Core) { Command = navigateLibrary },
        };

        MoveUpCommand = new RelayCommand<NavItem>(MoveUp);
        MoveDownCommand = new RelayCommand<NavItem>(MoveDown);
        UnpinCommand = new RelayCommand<NavItem>(Unpin);
        SelectPillCommand = new RelayCommand<SidebarPill>(p => CurrentPill = p);
        ToggleFolderCommand = new RelayCommand<SidebarFolderNode>(node =>
        {
            if (node == null) return;
            node.IsExpanded = !node.IsExpanded;
            node.Folder.IsExpanded = node.IsExpanded;
        });
        PinPlaylistCommand = new RelayCommand<Playlist>(p =>
        {
            if (p != null) PinPlaylist(p.Id, p.Name);
        });
        RenameFolderCommand = new RelayCommand<SidebarFolderNode>(n => _ = RenameFolderAsync(n));
        DeleteFolderCommand = new RelayCommand<SidebarFolderNode>(n => _ = DeleteFolderAsync(n));

        Rebuild();
    }

    public void MoveItem(NavItem item, int newIndex)
    {
        var oldIndex = Items.IndexOf(item);
        if (oldIndex < 0 || newIndex < 0 || newIndex >= Items.Count || oldIndex == newIndex) return;
        Items.Move(oldIndex, newIndex);
        PersistOrder();
    }

    public void Rebuild()
    {
        var pinnedData = _prefs.Current.PinnedItems;
        var pinnedItems = pinnedData.Select(ToNavItem).Where(i => i != null).Cast<NavItem>().ToList();

        if (pinnedItems.Count == 0 && _prefs.Current.AutoSuggestPinEnabled)
        {
            var suggestion = BuildAutoSuggestion();
            if (suggestion != null) pinnedItems.Add(suggestion);
        }

        var all = _coreItems.Concat(pinnedItems).ToList();
        var savedOrder = _prefs.Current.NavOrder;
        var ordered = savedOrder.Count > 0
            ? savedOrder
                .Select(key => all.FirstOrDefault(i => i.Key == key))
                .Where(i => i != null)
                .Cast<NavItem>()
                .Concat(all.Where(i => !savedOrder.Contains(i.Key)))
            : all;

        Items.Clear();
        foreach (var item in ordered) Items.Add(item);
        RefreshPlaylistLists();
    }

    private void Decorate(NavItem item, Playlist? playlist)
    {
        item.Playlist = playlist;
        item.ArtPath = playlist?.ArtPath;
        item.Subtitle = playlist == null ? null : $"Playlist • {playlist.Tracks.Count} tracks";
    }

    private NavItem? ToNavItem(PinnedItemData data)
    {
        if (data.Type == NavItemType.PinnedPlaylist && data.TargetPlaylistId.HasValue)
        {
            var id = data.TargetPlaylistId.Value;
            var playlist = _playlists.GetById(id);
            if (playlist == null) return null;   // playlist was deleted -> drop the ghost pin

            var item = new NavItem(data.Key, data.Label, MaterialIconKind.PlaylistMusic, NavItemType.PinnedPlaylist, id);
            item.Command = new RelayCommand(() => _navigateToPlaylist(id));
            Decorate(item, playlist);
            return item;
        }
        return null;
    }

    private NavItem? BuildAutoSuggestion()
    {
        var top = _playlists.GetAll()
            .OrderByDescending(p => p.Tracks.Count)
            .FirstOrDefault();
        if (top == null) return null;

        var item = new NavItem($"pin:{top.Id}", top.Name, MaterialIconKind.PlaylistMusic, NavItemType.PinnedPlaylist, top.Id)
        {
            IsAutoSuggested = true
        };
        item.Command = new RelayCommand(() => _navigateToPlaylist(top.Id));
        Decorate(item, top);
        return item;
    }

    public void PinPlaylist(Guid playlistId, string label)
    {
        var key = $"pin:{playlistId}";
        if (_prefs.Current.PinnedItems.Any(p => p.Key == key)) return;

        _prefs.Update(p => p.PinnedItems.Add(new PinnedItemData
        {
            Key = key,
            Type = NavItemType.PinnedPlaylist,
            Label = label,
            TargetPlaylistId = playlistId
        }));
        Rebuild();
    }

    public void UnpinPlaylist(Guid playlistId)
    {
        var key = $"pin:{playlistId}";
        _prefs.Update(p => p.PinnedItems.RemoveAll(x => x.Key == key));
        Rebuild();
    }

    public bool IsPlaylistPinned(Guid playlistId) =>
        _prefs.Current.PinnedItems.Any(p => p.Key == $"pin:{playlistId}");

    private void Unpin(NavItem? item)
    {
        if (item == null || !item.CanUnpin) return;
        if (item.Type == NavItemType.PinnedPlaylist && item.TargetPlaylistId.HasValue)
            UnpinPlaylist(item.TargetPlaylistId.Value);
    }

    public void SetActivePage(string page)
    {
        foreach (var item in Items)
            item.IsActive = item.Type == NavItemType.Core && item.Key == page;
    }

    public void SetPlaylistActive(Guid? playlistId)
    {
        foreach (var item in Items)
            item.IsActive = item.Type == NavItemType.PinnedPlaylist && item.TargetPlaylistId == playlistId;
    }

    public void RefreshPlaylistLists()
    {
        // 1. Collect all pinned IDs from preferences
        var pinnedIds = _prefs.Current.PinnedItems
            .Where(p => p.TargetPlaylistId.HasValue)
            .Select(p => p.TargetPlaylistId!.Value)
            .ToHashSet();

        // 2. Add IDs from the current Items list (catches non-persisted auto-suggested pins)
        foreach (var item in Items)
        {
            if (item.Type == NavItemType.PinnedPlaylist && item.TargetPlaylistId.HasValue)
                pinnedIds.Add(item.TargetPlaylistId.Value);
        }

        // 3. Build unpinned playlists list (excludes anything in the pinned rail)
        UnpinnedPlaylists.Clear();
        foreach (var playlist in _playlists.GetAll().Where(p => p.FolderId == null && !pinnedIds.Contains(p.Id)))
            UnpinnedPlaylists.Add(playlist);

        // 4. Build folder nodes (excludes anything in the pinned rail)
        FolderNodes.Clear();
        foreach (var folder in _playlists.GetAllFolders())
        {
            var node = new SidebarFolderNode(folder);
            foreach (var pl in _playlists.GetAll().Where(p => p.FolderId == folder.Id && !pinnedIds.Contains(p.Id)))
                node.Playlists.Add(pl);
            FolderNodes.Add(node);
        }

        // 5. Keep pinned rows' art/subtitle fresh
        foreach (var item in Items)
        {
            if (item.Type == NavItemType.PinnedPlaylist && item.TargetPlaylistId.HasValue)
                Decorate(item, _playlists.GetById(item.TargetPlaylistId!.Value));
        }
    }

    private void MoveUp(NavItem? item)
    {
        if (item == null) return;
        var index = Items.IndexOf(item);
        if (index <= 0) return;
        Items.Move(index, index - 1);
        PersistOrder();
    }

    private void MoveDown(NavItem? item)
    {
        if (item == null) return;
        var index = Items.IndexOf(item);
        if (index < 0 || index >= Items.Count - 1) return;
        Items.Move(index, index + 1);
        PersistOrder();
    }

    private void PersistOrder()
    {
        var order = Items.Select(i => i.Key).ToList();
        _prefs.Update(p => p.NavOrder = order);
    }

    public void MovePlaylistToFolder(Guid playlistId, Guid? folderId)
    {
        // Dragging into a folder unpins (Spotify semantics): one home at a time
        if (folderId.HasValue && IsPlaylistPinned(playlistId))
            UnpinPlaylist(playlistId);
        _playlists.MovePlaylistToFolder(playlistId, folderId);
        RefreshPlaylistLists();
    }

    private async Task RenameFolderAsync(SidebarFolderNode? node)
    {
        if (node == null) return;
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime d || d.MainWindow == null) return;
        var name = await new Views.CreateFolderDialog(node.Folder.Name).ShowDialog<string?>(d.MainWindow);
        if (string.IsNullOrWhiteSpace(name)) return;
        _playlists.RenameFolder(node.Folder.Id, name);
        RefreshPlaylistLists();
    }

    private async Task DeleteFolderAsync(SidebarFolderNode? node)
    {
        if (node == null) return;
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime d || d.MainWindow == null) return;
        var ok = await new Views.ConfirmDialog("Delete Folder?",
            $"Delete '{node.Folder.Name}'? Playlists inside move to the top level.").ShowDialog<bool>(d.MainWindow);
        if (!ok) return;

        var folderSnapshot = node.Folder;
        var memberIds = _playlists.GetAll()
            .Where(p => p.FolderId == folderSnapshot.Id)
            .Select(p => p.Id)
            .ToList();

        _playlists.RemoveFolder(folderSnapshot.Id);
        RefreshPlaylistLists();

        ToastService.Instance.Show(
            message: $"Folder '{folderSnapshot.Name}' deleted.",
            type: ToastType.Warning,
            durationMs: 8000,
            actionText: "Undo",
            actionCallback: () =>
            {
                _playlists.RestoreFolder(folderSnapshot, memberIds);
                RefreshPlaylistLists();
            },
            scope: "folder-delete");
    }
}