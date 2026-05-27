using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using NullWave.Helpers;
using NullWave.Models;
using NullWave.Services;
using NullWave.ViewModels.Base;
using System.Collections.Generic;

namespace NullWave.ViewModels;

public class LibraryViewModel : ViewModelBase
{
    private readonly LibraryService _library;
    private string _searchQuery = string.Empty;
    private Track? _selectedTrack;
    private SortField _currentSort = SortField.DateAdded;
    private bool _sortAscending = true;
    private TrackSource? _activeSourceFilter = null;

    public ObservableCollection<Track> Tracks { get; } = new();
    public Array SortOptions => Enum.GetValues(typeof(SortField));

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

        RemoveTrackCommand = new RelayCommand(RemoveTrack);
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
            _activeSourceFilter = null;
            Refresh();
        });

        ShowFavoritesCommand = new RelayCommand(ShowFavorites);
        ShowRecentCommand = new RelayCommand(ShowRecent);
        FilterYouTubeCommand = new RelayCommand(() => SetSourceFilter(TrackSource.YouTube));
        FilterSpotifyCommand = new RelayCommand(() => SetSourceFilter(TrackSource.Spotify));
        FilterSoundCloudCommand = new RelayCommand(() => SetSourceFilter(TrackSource.SoundCloud));
        FilterLocalCommand = new RelayCommand(() => SetSourceFilter(TrackSource.Local));
    }

    public void Refresh()
    {
        Tracks.Clear();

        IEnumerable<Track> results;

        if (_activeSourceFilter.HasValue)
        {
            results = _library.FilterBySource(_activeSourceFilter.Value);
        }
        else if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            results = _library.Search(SearchQuery, CurrentSort, SortAscending);
        }
        else
        {
            results = _library.GetSorted(CurrentSort, SortAscending);
        }

        foreach (var track in results)
            Tracks.Add(track);
    }

    private void SetSourceFilter(TrackSource source)
    {
        _activeSourceFilter = _activeSourceFilter == source ? null : source;
        Refresh();
    }

    private void ShowFavorites()
    {
        _activeSourceFilter = null;
        Tracks.Clear();
        foreach (var track in _library.GetFavorites())
            Tracks.Add(track);
    }

    private void ShowRecent()
    {
        _activeSourceFilter = null;
        Tracks.Clear();
        foreach (var track in _library.GetRecentlyAdded())
            Tracks.Add(track);
    }

    private void RemoveTrack()
    {
        if (SelectedTrack == null) return;
        _library.Remove(SelectedTrack.Id);
        SelectedTrack = null;
        Refresh();
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
}