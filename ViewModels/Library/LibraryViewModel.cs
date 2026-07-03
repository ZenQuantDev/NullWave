using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using NullWave.Models;
using NullWave.Services;

namespace NullWave.ViewModels;

public partial class LibraryViewModel : ObservableObject
{
    private readonly LibraryService _library;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private Track? _selectedTrack;

    [ObservableProperty]
    private SortField _currentSort = SortField.DateAdded;

    [ObservableProperty]
    private bool _sortAscending = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFavoritesView))]
    [NotifyPropertyChangedFor(nameof(IsRecentView))]
    [NotifyPropertyChangedFor(nameof(IsYouTubeFilter))]
    [NotifyPropertyChangedFor(nameof(IsLastFmFilter))]
    [NotifyPropertyChangedFor(nameof(IsSoundCloudFilter))]
    [NotifyPropertyChangedFor(nameof(IsLocalFilter))]
    private LibraryView _currentView = LibraryView.All;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsYouTubeFilter))]
    [NotifyPropertyChangedFor(nameof(IsLastFmFilter))]
    [NotifyPropertyChangedFor(nameof(IsSoundCloudFilter))]
    [NotifyPropertyChangedFor(nameof(IsLocalFilter))]
    private TrackSource? _activeSourceFilter = null;

    public enum LibraryView { All, Favorites, Recent, Source }

    public ObservableCollection<Track> Tracks { get; } = new();
    public Array SortOptions => Enum.GetValues(typeof(SortField));

    public bool IsFavoritesView => CurrentView == LibraryView.Favorites;
    public bool IsRecentView => CurrentView == LibraryView.Recent;
    public bool IsYouTubeFilter => CurrentView == LibraryView.Source && ActiveSourceFilter == TrackSource.YouTube;
    public bool IsLastFmFilter => CurrentView == LibraryView.Source && ActiveSourceFilter == TrackSource.LastFm;
    public bool IsSoundCloudFilter => CurrentView == LibraryView.Source && ActiveSourceFilter == TrackSource.SoundCloud;
    public bool IsLocalFilter => CurrentView == LibraryView.Source && ActiveSourceFilter == TrackSource.Local;

    public event Action<Track>? TrackDetailRequested;
    public event Action<Track>? PlayTrackRequested;

    public LibraryViewModel(LibraryService library)
    {
        _library = library;
        Refresh();
    }

    partial void OnSearchQueryChanged(string value) => Refresh();
    partial void OnCurrentSortChanged(SortField value) => Refresh();
    partial void OnSortAscendingChanged(bool value) => Refresh();

    public void Refresh()
    {
        Tracks.Clear();
        IEnumerable<Track> results;

        switch (CurrentView)
        {
            case LibraryView.Favorites:
                results = _library.GetFavorites();
                break;
            case LibraryView.Recent:
                results = _library.GetRecentlyAdded();
                break;
            case LibraryView.Source:
                results = ActiveSourceFilter.HasValue
                    ? _library.FilterBySource(ActiveSourceFilter.Value)
                    : _library.GetSorted(CurrentSort, SortAscending);
                break;
            default:
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
        if (ActiveSourceFilter == source)
        {
            CurrentView = LibraryView.All;
            ActiveSourceFilter = null;
        }
        else
        {
            CurrentView = LibraryView.Source;
            ActiveSourceFilter = source;
        }
        Refresh();
    }

    [RelayCommand]
    private void RemoveTrack(Track? t)
    {
        var target = t ?? SelectedTrack;
        if (target == null) return;
        _library.Remove(target.Id);
        if (SelectedTrack?.Id == target.Id) SelectedTrack = null;
        Refresh();
    }

    [RelayCommand]
    private void ToggleFavorite()
    {
        if (SelectedTrack == null) return;
        _library.ToggleFavorite(SelectedTrack.Id);
        Refresh();
    }

    [RelayCommand]
    private void AddToQueue()
    {
        if (SelectedTrack == null) return;
        _library.AddToQueue(SelectedTrack.Id);
    }

    [RelayCommand]
    private void RecordPlay()
    {
        if (SelectedTrack == null) return;
        _library.RecordPlay(SelectedTrack.Id);
        Refresh();
    }

    [RelayCommand] private void SortByTitle() => CurrentSort = SortField.Title;
    [RelayCommand] private void SortByArtist() => CurrentSort = SortField.Artist;
    [RelayCommand] private void SortByDate() => CurrentSort = SortField.DateAdded;
    [RelayCommand] private void SortByPlayCount() => CurrentSort = SortField.PlayCount;
    [RelayCommand] private void FocusSearch() => SearchQuery = string.Empty;

    [RelayCommand]
    private void ClearSearch()
    {
        SearchQuery = string.Empty;
        CurrentView = LibraryView.All;
        ActiveSourceFilter = null;
        Refresh();
    }

    [RelayCommand] private void ShowFavorites() { CurrentView = LibraryView.Favorites; ActiveSourceFilter = null; Refresh(); }
    [RelayCommand] private void ShowRecent() { CurrentView = LibraryView.Recent; ActiveSourceFilter = null; Refresh(); }
    [RelayCommand] private void FilterYouTube() => SetSourceFilter(TrackSource.YouTube);
    [RelayCommand] private void FilterSpotify() => SetSourceFilter(TrackSource.Spotify);
    [RelayCommand] private void FilterSoundCloud() => SetSourceFilter(TrackSource.SoundCloud);
    [RelayCommand] private void FilterLocal() => SetSourceFilter(TrackSource.Local);
    [RelayCommand] private void FilterLastFm() => SetSourceFilter(TrackSource.LastFm);
    [RelayCommand] private void OpenDetail() { if (SelectedTrack != null) TrackDetailRequested?.Invoke(SelectedTrack); }
    [RelayCommand] private void PlayTrack(Track? t) { if (t != null) PlayTrackRequested?.Invoke(t); }

    [RelayCommand]
    private async Task CopyUrlAsync()
    {
        var url = SelectedTrack?.Url ?? SelectedTrack?.FilePath;
        if (string.IsNullOrEmpty(url)) return;
        try
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
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
}