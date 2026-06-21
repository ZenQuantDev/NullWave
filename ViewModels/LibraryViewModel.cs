using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using NullWave.Helpers;
using NullWave.Models;
using NullWave.Services;
using NullWave.ViewModels.Base;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Serilog;

namespace NullWave.ViewModels;

public class LibraryViewModel : ViewModelBase
{
    private readonly LibraryService _library;
    private string _searchQuery = string.Empty;
    private Track? _selectedTrack;
    private SortField _currentSort = SortField.DateAdded;
    private bool _sortAscending = true;

    // ── Filter Persistence State ──────────────────────────────────────────
    private enum LibraryView { All, Favorites, Recent, Source }
    private LibraryView _currentView = LibraryView.All;
    private TrackSource? _activeSourceFilter = null;

    public ObservableCollection<Track> Tracks { get; } = new();
    public Array SortOptions => Enum.GetValues(typeof(SortField));

    // ── Filter active-state properties ──────────────────────────────────
    // Bound by SidebarView's code-behind to highlight whichever filter
    // button matches the currently active view. Previously these didn't
    // exist, so the filter buttons worked (Tracks correctly filtered) but
    // never visually highlighted, unlike the four page-nav buttons which
    // already had this wired via CurrentPage.
    public bool IsFavoritesView => _currentView == LibraryView.Favorites;
    public bool IsRecentView => _currentView == LibraryView.Recent;
    public bool IsYouTubeFilter => _currentView == LibraryView.Source && _activeSourceFilter == TrackSource.YouTube;
    public bool IsLastFmFilter => _currentView == LibraryView.Source && _activeSourceFilter == TrackSource.LastFm;
    public bool IsSoundCloudFilter => _currentView == LibraryView.Source && _activeSourceFilter == TrackSource.SoundCloud;
    public bool IsLocalFilter => _currentView == LibraryView.Source && _activeSourceFilter == TrackSource.Local;

    // Existing commands
    public ICommand RemoveTrackCommand { get; }
    public ICommand ToggleFavoriteCommand { get; }
    public ICommand AddToQueueCommand { get; }
    public ICommand RecordPlayCommand { get; }

    // Sort commands
    public ICommand SortByTitleCommand { get; }
    public ICommand SortByArtistCommand { get; }
    public ICommand SortByDateCommand { get; }
    public ICommand SortByPlayCountCommand { get; }

    // Search commands
    public ICommand FocusSearchCommand { get; }
    public ICommand ClearSearchCommand { get; }

    // Filter commands
    public ICommand ShowFavoritesCommand { get; }
    public ICommand ShowRecentCommand { get; }
    public ICommand FilterYouTubeCommand { get; }
    public ICommand FilterSpotifyCommand { get; }
    public ICommand FilterSoundCloudCommand { get; }
    public ICommand FilterLocalCommand { get; }
    public ICommand FilterLastFmCommand { get; }

    // Context menu commands
    public ICommand CopyUrlCommand { get; }
    public ICommand AddToPlaylistCommand { get; }
    public ICommand OpenDetailCommand { get; }
    public ICommand PlayTrackCommand { get; }

    public event Action<Track>? TrackDetailRequested;
    public event Action<Track>? PlayTrackRequested;

    public string SearchQuery
    {
        get => _searchQuery;
        set { _searchQuery = value; OnPropertyChanged(); Refresh(); }
    }

    public Track? SelectedTrack
    {
        get => _selectedTrack;
        set { _selectedTrack = value; OnPropertyChanged(); }
    }

    public SortField CurrentSort
    {
        get => _currentSort;
        set { _currentSort = value; OnPropertyChanged(); Refresh(); }
    }

    public bool SortAscending
    {
        get => _sortAscending;
        set { _sortAscending = value; OnPropertyChanged(); Refresh(); }
    }

    public LibraryViewModel(LibraryService library)
    {
        _library = library;

        RemoveTrackCommand = new RelayCommand<Track>(t =>
        {
            var target = t ?? SelectedTrack;
            if (target == null) return;
            _library.Remove(target.Id);
            if (SelectedTrack?.Id == target.Id) SelectedTrack = null;
            Refresh();
        });

        ToggleFavoriteCommand = new RelayCommand(ToggleFavorite);
        AddToQueueCommand = new RelayCommand(AddToQueue);
        RecordPlayCommand = new RelayCommand(RecordPlay);

        SortByTitleCommand = new RelayCommand(() => CurrentSort = SortField.Title);
        SortByArtistCommand = new RelayCommand(() => CurrentSort = SortField.Artist);
        SortByDateCommand = new RelayCommand(() => CurrentSort = SortField.DateAdded);
        SortByPlayCountCommand = new RelayCommand(() => CurrentSort = SortField.PlayCount);

        FocusSearchCommand = new RelayCommand(() => SearchQuery = string.Empty);

        ClearSearchCommand = new RelayCommand(() =>
        {
            SearchQuery = string.Empty;
            _currentView = LibraryView.All;
            _activeSourceFilter = null;
            Refresh();
            NotifyFilterStateChanged();
        });

        ShowFavoritesCommand = new RelayCommand(ShowFavorites);
        ShowRecentCommand = new RelayCommand(ShowRecent);

        FilterYouTubeCommand = new RelayCommand(() => SetSourceFilter(TrackSource.YouTube));
        FilterSpotifyCommand = new RelayCommand(() => SetSourceFilter(TrackSource.Spotify));
        FilterSoundCloudCommand = new RelayCommand(() => SetSourceFilter(TrackSource.SoundCloud));
        FilterLocalCommand = new RelayCommand(() => SetSourceFilter(TrackSource.Local));
        FilterLastFmCommand = new RelayCommand(() => SetSourceFilter(TrackSource.LastFm));

        CopyUrlCommand = new RelayCommand(CopyUrl);
        OpenDetailCommand = new RelayCommand(OpenDetail);
        AddToPlaylistCommand = new RelayCommand(() => { });
        PlayTrackCommand = new RelayCommand<Track>(t => { if (t != null) PlayTrackRequested?.Invoke(t); });
    }

    // ── Refresh Logic (Now respects the active view state) ────────────────
    public void Refresh()
    {
        Tracks.Clear();
        IEnumerable<Track> results;

        switch (_currentView)
        {
            case LibraryView.Favorites:
                results = _library.GetFavorites();
                break;
            case LibraryView.Recent:
                results = _library.GetRecentlyAdded();
                break;
            case LibraryView.Source:
                results = _activeSourceFilter.HasValue
                    ? _library.FilterBySource(_activeSourceFilter.Value)
                    : _library.GetSorted(CurrentSort, SortAscending);
                break;
            default: // LibraryView.All
                if (!string.IsNullOrWhiteSpace(SearchQuery))
                    results = _library.Search(SearchQuery, CurrentSort, SortAscending);
                else
                    results = _library.GetSorted(CurrentSort, SortAscending);
                break;
        }

        foreach (var track in results)
            Tracks.Add(track);
    }

    private void SetSourceFilter(TrackSource source)
    {
        if (_activeSourceFilter == source)
        {
            _currentView = LibraryView.All;
            _activeSourceFilter = null;
        }
        else
        {
            _currentView = LibraryView.Source;
            _activeSourceFilter = source;
        }
        Refresh();
        NotifyFilterStateChanged();
        Log.Information("[LibraryViewModel] DIAGNOSTIC: SetSourceFilter({Source}) → view={View}, filter={Filter}, Tracks.Count={Count}",
            source, _currentView, _activeSourceFilter, Tracks.Count);
    }

    private void ShowFavorites()
    {
        _currentView = LibraryView.Favorites;
        _activeSourceFilter = null;
        Refresh();
        NotifyFilterStateChanged();
    }

    private void ShowRecent()
    {
        _currentView = LibraryView.Recent;
        _activeSourceFilter = null;
        Refresh();
        NotifyFilterStateChanged();
    }

    /// <summary>
    /// Fires change notifications for all six filter-state booleans at
    /// once. Called after any operation that changes _currentView or
    /// _activeSourceFilter, so SidebarView's code-behind (subscribed to
    /// Library.PropertyChanged) knows to re-evaluate button highlight state.
    /// </summary>
    private void NotifyFilterStateChanged()
    {
        OnPropertyChanged(nameof(IsFavoritesView));
        OnPropertyChanged(nameof(IsRecentView));
        OnPropertyChanged(nameof(IsYouTubeFilter));
        OnPropertyChanged(nameof(IsLastFmFilter));
        OnPropertyChanged(nameof(IsSoundCloudFilter));
        OnPropertyChanged(nameof(IsLocalFilter));
    }

    private void ToggleFavorite()
    {
        if (SelectedTrack == null) return;
        _library.ToggleFavorite(SelectedTrack.Id);
        Refresh();
    }

    private void AddToQueue()
    {
        if (SelectedTrack == null) return;
        _library.AddToQueue(SelectedTrack.Id);
    }

    private void RecordPlay()
    {
        if (SelectedTrack == null) return;
        _library.RecordPlay(SelectedTrack.Id);
        Refresh();
    }

    private async void CopyUrl()
    {
        var url = SelectedTrack?.Url ?? SelectedTrack?.FilePath;
        if (string.IsNullOrEmpty(url)) return;

        try
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                && desktop.MainWindow != null)
            {
                var clipboard = TopLevel.GetTopLevel(desktop.MainWindow)?.Clipboard;
                if (clipboard != null)
                {
                    await clipboard.SetTextAsync(url);
                    Log.Information("URL copied to clipboard: {Url}", url);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to copy URL to clipboard");
        }
    }

    private void OpenDetail()
    {
        if (SelectedTrack != null)
            TrackDetailRequested?.Invoke(SelectedTrack);
    }
}