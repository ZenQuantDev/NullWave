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
    private readonly SmartPlaylistService _smartPlaylists;
    private readonly Action<Guid> _navigateToPlaylist;
    private readonly Action<string>? _navigateToSmartPlaylist;
    private readonly List<NavItem> _coreItems;
    private List<string> _liveOrder = new();

    private SidebarPill _currentPill = SidebarPill.Playlists;
    public SidebarPill CurrentPill
    {
        get => _currentPill;
        set { _currentPill = value; OnPropertyChanged(); }
    }

    public ObservableCollection<NavItem> Items { get; } = new();
    public ObservableCollection<Playlist> UnpinnedPlaylists { get; } = new();
    public ObservableCollection<SidebarFolderNode> FolderNodes { get; } = new();

    private bool _isReorderDragging;
    public bool IsReorderDragging
    {
        get => _isReorderDragging;
        set { _isReorderDragging = value; OnPropertyChanged(); }
    }

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
        SmartPlaylistService smartPlaylists,
        Func<ICommand> navigateLibrary,
        Func<ICommand> navigatePlaylists,
        Func<ICommand> navigateRadio,
        Func<ICommand> navigateAudiobooks,
        Action<Guid> navigateToPlaylist,
        Action<string> navigateToSmartPlaylist)
    {
        _prefs = prefs;
        _playlists = playlists;
        _smartPlaylists = smartPlaylists;
        _navigateToPlaylist = navigateToPlaylist;
        _navigateToSmartPlaylist = navigateToSmartPlaylist;

        _coreItems = new List<NavItem>
        {
            new("Library", "Library", MaterialIconKind.Bookshelf, NavItemType.Core)
            {
                Command = new RelayCommand(() => { navigateLibrary()?.Execute(null); SetActivePage("Library"); })
            },
            new("Radio", "Radio", MaterialIconKind.Radio, NavItemType.Core)
            {
                Command = new RelayCommand(() => { navigateRadio()?.Execute(null); SetActivePage("Radio"); })
            },
            new("Audiobooks", "Audiobooks", MaterialIconKind.BookOpenPageVariant, NavItemType.Core)
            {
                Command = new RelayCommand(() => { navigateAudiobooks()?.Execute(null); SetActivePage("Audiobooks"); })
            },
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
            _playlists.UpdateFolder(node.Folder);
        });
        PinPlaylistCommand = new RelayCommand<Playlist>(p => { if (p != null) PinPlaylist(p.Id, p.Name); });
        RenameFolderCommand = new SafeRelayCommand<SidebarFolderNode>(async n => await RenameFolderAsync(n), errorScope: "folder");
        DeleteFolderCommand = new SafeRelayCommand<SidebarFolderNode>(async n => await DeleteFolderAsync(n), errorScope: "folder");

        Items.CollectionChanged += (_, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Move)
                PersistOrder();
        };

        Rebuild();
    }

    internal static string NavKey(NavItem item) =>
        item.Type == NavItemType.PinnedPlaylist && item.TargetPlaylistId.HasValue
            ? $"pin:{item.TargetPlaylistId.Value}"
            : item.Key;

    public void MoveItem(NavItem draggedItem, int targetIndex)
    {
        if (draggedItem == null) return;
        var draggedKey = NavKey(draggedItem);
        var actualDraggedItem = Items.FirstOrDefault(item => NavKey(item) == draggedKey);
        if (actualDraggedItem == null) return;

        var oldIndex = Items.IndexOf(actualDraggedItem);
        if (oldIndex < 0 || targetIndex < 0 || targetIndex >= Items.Count || oldIndex == targetIndex) return;

        Items.Move(oldIndex, targetIndex);
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

        // Core + pinned only. Smart playlists live inside the AI Playlists folder (see RefreshPlaylistLists).
        var all = _coreItems.Concat(pinnedItems).ToList();
        var savedOrder = _liveOrder.Count > 0 ? _liveOrder : _prefs.Current.NavOrder;

        var ordered = savedOrder.Count > 0
            ? savedOrder
                .Select(key => all.FirstOrDefault(i => NavKey(i) == key))
                .Where(i => i != null)
                .Cast<NavItem>()
                .Concat(all.Where(i => !savedOrder.Contains(NavKey(i))))
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
        if (playlist != null && item.Type == NavItemType.PinnedPlaylist && item.Label != playlist.Name)
            item.Label = playlist.Name;
    }

    private NavItem? ToNavItem(PinnedItemData data)
    {
        if (data.Type == NavItemType.PinnedPlaylist && data.TargetPlaylistId.HasValue)
        {
            var id = data.TargetPlaylistId.Value;
            var playlist = _playlists.GetById(id);
            if (playlist == null) return null;

            var item = new NavItem(data.Key, data.Label, MaterialIconKind.PlaylistMusic, NavItemType.PinnedPlaylist, id);
            item.Command = new RelayCommand(() => _navigateToPlaylist(id));
            Decorate(item, playlist);
            return item;
        }
        return null;
    }

    private NavItem? BuildAutoSuggestion()
    {
        var top = _playlists.GetAll().OrderByDescending(p => p.Tracks.Count).FirstOrDefault();
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
        if (_playlists.GetById(playlistId) == null) return; // guard: virtual/smart playlists can't be pinned
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
        var pinnedIds = _prefs.Current.PinnedItems
            .Where(p => p.TargetPlaylistId.HasValue)
            .Select(p => p.TargetPlaylistId!.Value)
            .ToHashSet();

        foreach (var item in Items)
        {
            if (item.Type == NavItemType.PinnedPlaylist && item.TargetPlaylistId.HasValue)
                pinnedIds.Add(item.TargetPlaylistId.Value);
        }

        UnpinnedPlaylists.Clear();
        foreach (var playlist in _playlists.GetAll().Where(p => p.FolderId == null && !pinnedIds.Contains(p.Id)))
            UnpinnedPlaylists.Add(playlist);

        FolderNodes.Clear();
        foreach (var folder in _playlists.GetAllFolders())
        {
            var node = new SidebarFolderNode(folder);
            foreach (var pl in _playlists.GetAll().Where(p => p.FolderId == folder.Id))
                node.Playlists.Add(pl);
            FolderNodes.Add(node);
        }

        InjectSmartPlaylistsIntoFolder();

        foreach (var item in Items)
        {
            if (item.Type == NavItemType.PinnedPlaylist && item.TargetPlaylistId.HasValue)
                Decorate(item, _playlists.GetById(item.TargetPlaylistId!.Value));
        }

        bool pinsDirty = false;
        foreach (var pin in _prefs.Current.PinnedItems)
        {
            if (!pin.TargetPlaylistId.HasValue) continue;
            var pl = _playlists.GetById(pin.TargetPlaylistId.Value);
            if (pl != null && pin.Label != pl.Name)
            {
                pin.Label = pl.Name;
                pinsDirty = true;
            }
        }

        if (pinsDirty)
        {
            var updatedPins = _prefs.Current.PinnedItems.ToList();
            _prefs.Update(p => p.PinnedItems = updatedPins);
        }
    }

    /// <summary>
    /// Smart playlists are virtual (no DB rows). We surface them as read-only placeholder
    /// Playlist objects inside the "AI Playlists" folder so they collapse/expand like
    /// normal playlists and are never pinned to the top level.
    /// </summary>
    private void InjectSmartPlaylistsIntoFolder()
    {
        var node = FolderNodes.FirstOrDefault(n => string.Equals(n.Folder.Name, "AI Playlists", StringComparison.OrdinalIgnoreCase));
        if (node == null)
        {
            if (FolderNodes.Count == 0)
            {
                node = new SidebarFolderNode(new PlaylistFolder { Name = "AI Playlists" });
                FolderNodes.Add(node);
            }
            else return; // no AI folder and other folders exist: leave smart playlists hidden rather than misfiled
        }

        foreach (var def in _smartPlaylists.All)
        {
            var virtualId = SmartPlaylistIds.ToGuid(def.Id);
            if (virtualId == Guid.Empty) continue;

            var placeholder = new Playlist
            {
                Id = virtualId,
                Name = GetSmartDisplayName(def),
                Description = "Smart playlist · auto-updates"
            };

            foreach (var track in _smartPlaylists.Resolve(def))
                placeholder.Tracks.Add(track);

            node.Playlists.Add(placeholder);
        }
    }

    private static string GetSmartDisplayName(SmartPlaylistDefinition def)
    {
        var loc = LocalizationService.Instance[def.NameKey];
        if (!string.IsNullOrEmpty(loc) && !loc.StartsWith("[")) return loc;
        return def.Id switch
        {
            "smart.recently-added"      => "Recently Added",
            "smart.most-played"         => "Most Played",
            "smart.forgotten-favorites" => "Forgotten Favorites",
            _ => def.NameKey
        };
    }

    private void MoveUp(NavItem? item)
    {
        if (item == null) return;
        var index = Items.IndexOf(item);
        if (index <= 0) return;
        Items.Move(index, index - 1);
    }

    private void MoveDown(NavItem? item)
    {
        if (item == null) return;
        var index = Items.IndexOf(item);
        if (index < 0 || index >= Items.Count - 1) return;
        Items.Move(index, index + 1);
    }

    private void PersistOrder()
    {
        var order = Items.Select(NavKey).ToList();
        _liveOrder = order;
        _prefs.Update(p => p.NavOrder = order);
    }

    public void MovePlaylistToFolder(Guid playlistId, Guid? folderId)
    {
        if (folderId.HasValue && IsPlaylistPinned(playlistId))
            UnpinPlaylist(playlistId);

        _playlists.MovePlaylistToFolder(playlistId, folderId);
        RefreshPlaylistLists();
    }

    private async Task RenameFolderAsync(SidebarFolderNode? node)
    {
        if (node == null) return;

        var dialog = new Views.CreateFolderDialog(node.Folder.Name);
        var name = await DialogService.Instance.ShowDialogAsync<string?>(dialog);

        if (string.IsNullOrWhiteSpace(name)) return;

        _playlists.RenameFolder(node.Folder.Id, name);
        RefreshPlaylistLists();
    }

    private async Task DeleteFolderAsync(SidebarFolderNode? node)
    {
        if (node == null) return;

        var dialog = new Views.ConfirmDialog(
            "Delete Folder?",
            $"Delete '{node.Folder.Name}'? Playlists inside move to the top level."
        );
        var ok = await DialogService.Instance.ShowDialogAsync<bool>(dialog);

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