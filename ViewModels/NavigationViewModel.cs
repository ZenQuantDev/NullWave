using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using NullWave.Helpers;
using NullWave.Models;
using NullWave.Services;
using NullWave.ViewModels.Base;

namespace NullWave.ViewModels;

public class NavigationViewModel : ViewModelBase
{
    private readonly PreferencesService _prefs;
    private readonly PlaylistService _playlists;
    private readonly Action<Guid> _navigateToPlaylist;
    private readonly List<NavItem> _coreItems;

    public ObservableCollection<NavItem> Items { get; } = new();

    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }
    public ICommand UnpinCommand { get; }

    public NavigationViewModel(
        PreferencesService prefs,
        PlaylistService playlists,
        ICommand navigateLibrary,
        ICommand navigatePlaylists,
        ICommand navigateQueue,
        ICommand navigateStats,
        Action<Guid> navigateToPlaylist)
    {
        _prefs = prefs;
        _playlists = playlists;
        _navigateToPlaylist = navigateToPlaylist;

        _coreItems = new List<NavItem>
        {
            new("Library",   "Library",   "Bookshelf",    NavItemType.Core) { Command = navigateLibrary },
            new("Playlists", "Playlists", "PlaylistMusic", NavItemType.Core) { Command = navigatePlaylists },
            new("Queue",     "Queue",     "PlaylistPlay",  NavItemType.Core) { Command = navigateQueue },
            new("Stats",     "Stats",     "ChartBar",      NavItemType.Core) { Command = navigateStats },
        };

        MoveUpCommand = new RelayCommand<NavItem>(MoveUp);
        MoveDownCommand = new RelayCommand<NavItem>(MoveDown);
        UnpinCommand = new RelayCommand<NavItem>(Unpin);

        Rebuild();
    }

    /// <summary>
    /// Rebuilds the full Items list from core items + persisted pins (+ the
    /// auto-suggested pin, if no real pins exist yet), then applies the
    /// saved display order. Call after any pin/unpin so the auto-suggestion
    /// correctly appears/disappears.
    /// </summary>
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
    }

    private NavItem? ToNavItem(PinnedItemData data)
    {
        if (data.Type == NavItemType.PinnedPlaylist && data.TargetPlaylistId.HasValue)
        {
            var id = data.TargetPlaylistId.Value;
            var item = new NavItem(data.Key, data.Label, "PlaylistMusic", NavItemType.PinnedPlaylist, id);
            item.Command = new RelayCommand(() => _navigateToPlaylist(id));
            return item;
        }
        // SavedSearch pins: not wired in this pass — data model supports it,
        // command wiring deferred until the search-bar "pin" affordance exists.
        return null;
    }

    private NavItem? BuildAutoSuggestion()
    {
        var top = _playlists.GetAll()
            .OrderByDescending(p => p.Tracks.Count) // proxy for "most substantial"
            .FirstOrDefault();
        if (top == null) return null;

        var item = new NavItem($"pin:{top.Id}", top.Name, "PlaylistMusic", NavItemType.PinnedPlaylist, top.Id)
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

    public void SetQueueActive(bool active)
    {
        var item = Items.FirstOrDefault(i => i.Key == "Queue");
        if (item != null) item.IsActive = active;
    }

    public void SetPlaylistActive(Guid? playlistId)
    {
        foreach (var item in Items)
            item.IsActive = item.Type == NavItemType.PinnedPlaylist
                && item.TargetPlaylistId == playlistId;
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