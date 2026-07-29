using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
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

    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }
    public ICommand UnpinCommand { get; }
    public ICommand SelectPillCommand { get; }

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

    private NavItem? ToNavItem(PinnedItemData data)
    {
        if (data.Type == NavItemType.PinnedPlaylist && data.TargetPlaylistId.HasValue)
        {
            var id = data.TargetPlaylistId.Value;
            var item = new NavItem(data.Key, data.Label, MaterialIconKind.PlaylistMusic, NavItemType.PinnedPlaylist, id);
            item.Command = new RelayCommand(() => _navigateToPlaylist(id));
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
        UnpinnedPlaylists.Clear();
        var pinnedIds = _prefs.Current.PinnedItems
            .Where(p => p.TargetPlaylistId.HasValue)
            .Select(p => p.TargetPlaylistId!.Value)
            .ToHashSet();

        foreach (var playlist in _playlists.GetAll().Where(p => !pinnedIds.Contains(p.Id)))
            UnpinnedPlaylists.Add(playlist);
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
}