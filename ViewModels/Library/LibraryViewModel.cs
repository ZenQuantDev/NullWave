using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using NullWave.Models;
using NullWave.Services;
using NullWave.Helpers;
using NullWave.Helpers.Logging;

namespace NullWave.ViewModels;

public partial class LibraryViewModel : ObservableObject
{
    private readonly LibraryService _library;
    private CancellationTokenSource? _stateCts;

    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private Track? _selectedTrack;
    [ObservableProperty] private SortField _currentSort = SortField.DateAdded;
    [ObservableProperty] private bool _sortAscending = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFavoritesView))]
    [NotifyPropertyChangedFor(nameof(IsRecentView))]
    [NotifyPropertyChangedFor(nameof(IsYouTubeFilter))]
    [NotifyPropertyChangedFor(nameof(IsLastFmFilter))]
    [NotifyPropertyChangedFor(nameof(IsSoundCloudFilter))]
    [NotifyPropertyChangedFor(nameof(IsLocalFilter))]
    [NotifyPropertyChangedFor(nameof(IsSpotifyFilter))]
    private LibraryView _currentView = LibraryView.All;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsYouTubeFilter))]
    [NotifyPropertyChangedFor(nameof(IsLastFmFilter))]
    [NotifyPropertyChangedFor(nameof(IsSoundCloudFilter))]
    [NotifyPropertyChangedFor(nameof(IsLocalFilter))]
    [NotifyPropertyChangedFor(nameof(IsSpotifyFilter))]
    private TrackSource? _activeSourceFilter = null;

    public enum LibraryView { All, Favorites, Recent, Source }

    public BulkObservableCollection<Track> Tracks { get; } = new();
    public Array SortOptions => Enum.GetValues(typeof(SortField));

    public bool IsFavoritesView => CurrentView == LibraryView.Favorites;
    public bool IsRecentView => CurrentView == LibraryView.Recent;
    public bool IsYouTubeFilter => CurrentView == LibraryView.Source && ActiveSourceFilter == TrackSource.YouTube;
    public bool IsLastFmFilter => CurrentView == LibraryView.Source && ActiveSourceFilter == TrackSource.LastFm;
    public bool IsSoundCloudFilter => CurrentView == LibraryView.Source && ActiveSourceFilter == TrackSource.SoundCloud;
    public bool IsLocalFilter => CurrentView == LibraryView.Source && ActiveSourceFilter == TrackSource.Local;
    public bool IsSpotifyFilter => CurrentView == LibraryView.Source && ActiveSourceFilter == TrackSource.Spotify;

    public event Action<Track>? TrackDetailRequested;
    public event Action<Track>? PlayTrackRequested;

    public LibraryViewModel(LibraryService library)
    {
        _library = library;
        TriggerRefresh(debounce: false);
    }

    partial void OnSearchQueryChanged(string value) => TriggerRefresh(debounce: true);
    partial void OnCurrentSortChanged(SortField value) => TriggerRefresh(debounce: false);
    partial void OnSortAscendingChanged(bool value) => TriggerRefresh(debounce: false);
    partial void OnCurrentViewChanged(LibraryView value) => TriggerRefresh(debounce: false);
    partial void OnActiveSourceFilterChanged(TrackSource? value) => TriggerRefresh(debounce: false);

    public void Refresh() => TriggerRefresh(debounce: false);

    private void TriggerRefresh(bool debounce = false)
    {
        _stateCts?.Cancel();
        _stateCts?.Dispose();
        _stateCts = new CancellationTokenSource();
        var token = _stateCts.Token;

        string query = SearchQuery;
        SortField sort = CurrentSort;
        bool ascending = SortAscending;
        LibraryView view = CurrentView;
        TrackSource? filter = ActiveSourceFilter;

        _ = Task.Run(async () =>
        {
            try
            {
                if (debounce)
                {
                    await Task.Delay(300, token);
                }

                var (results, wasSearch) = FetchLibraryDataInternal(query, sort, ascending, view, filter);

                if (token.IsCancellationRequested) return;

                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (token.IsCancellationRequested) return;

                    Guid? previousSelectedId = SelectedTrack?.Id;

                    Tracks.ReplaceAll(results);

                    if (previousSelectedId.HasValue)
                    {
                        SelectedTrack = Tracks.FirstOrDefault(t => t.Id == previousSelectedId.Value);
                    }

                    if (wasSearch)
                    {
                        NullActionLogger.SearchPerformed(query, Tracks.Count, "LibraryViewModel");
                    }
                });
            }
            catch (TaskCanceledException) { }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed background state synchronization inside LibraryViewModel.");
            }
        });
    }

    private (IEnumerable<Track> Results, bool WasSearchExecuted) FetchLibraryDataInternal(
        string query, SortField sort, bool ascending, LibraryView view, TrackSource? filter)
    {
        IEnumerable<Track> results;
        bool wasSearch = false;

        switch (view)
        {
            case LibraryView.Favorites:
                results = _library.GetFavorites();
                break;
            case LibraryView.Recent:
                results = _library.GetRecentlyAdded();
                break;
            case LibraryView.Source:
                results = filter.HasValue
                    ? _library.FilterBySource(filter.Value)
                    : _library.GetSorted(sort, ascending);
                break;
            default:
                if (!string.IsNullOrWhiteSpace(query))
                {
                    results = _library.Search(query, sort, ascending);
                    wasSearch = true;
                }
                else
                {
                    results = _library.GetSorted(sort, ascending);
                }
                break;
        }

        return (results, wasSearch);
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
    }

    [RelayCommand]
    private async Task RemoveTrackAsync(Track? t)
    {
        var target = t ?? SelectedTrack;
        if (target == null) return;

        Guid targetId = target.Id;
        if (SelectedTrack?.Id == targetId) SelectedTrack = null;

        await Task.Run(() => _library.Remove(targetId));

        NullActionLogger.TrackRemoved(targetId.ToString(), "LibraryViewModel");
        TriggerRefresh(debounce: false);
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync()
    {
        if (SelectedTrack == null) return;

        Guid targetId = SelectedTrack.Id;
        bool expectedNewState = !SelectedTrack.IsFavorite;

        await Task.Run(() => _library.ToggleFavorite(targetId));

        NullActionLogger.FavoriteToggled(targetId.ToString(), expectedNewState, "LibraryViewModel");
        TriggerRefresh(debounce: false);
    }

    [RelayCommand]
    private async Task RecordPlayAsync()
    {
        if (SelectedTrack == null) return;
        
        Guid targetId = SelectedTrack.Id;
        await Task.Run(() => _library.RecordPlay(targetId));
        
        TriggerRefresh(debounce: false);
    }

    [RelayCommand]
    private void AddToQueue()
    {
        if (SelectedTrack == null) return;
        _library.AddToQueue(SelectedTrack.Id);
        NullActionLogger.User("AddToQueue", SelectedTrack.Id.ToString(), "LibraryViewModel");
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
    }

    [RelayCommand] 
    private void ShowFavorites() 
    { 
        CurrentView = LibraryView.Favorites; 
        ActiveSourceFilter = null; 
    }

    [RelayCommand] 
    private void ShowRecent() 
    { 
        CurrentView = LibraryView.Recent; 
        ActiveSourceFilter = null; 
    }

    [RelayCommand] private void FilterYouTube() => SetSourceFilter(TrackSource.YouTube);
    [RelayCommand] private void FilterSpotify() => SetSourceFilter(TrackSource.Spotify);
    [RelayCommand] private void FilterSoundCloud() => SetSourceFilter(TrackSource.SoundCloud);
    [RelayCommand] private void FilterLocal() => SetSourceFilter(TrackSource.Local);
    [RelayCommand] private void FilterLastFm() => SetSourceFilter(TrackSource.LastFm);
    
    [RelayCommand] private void OpenDetail() { if (SelectedTrack != null) TrackDetailRequested?.Invoke(SelectedTrack); }

    [RelayCommand]
    private void PlayTrack(Track? t)
    {
        if (t != null)
        {
            NullActionLogger.TrackPlayed(t.Id.ToString(), t.Title, t.Artist, "LibraryViewModel");
            PlayTrackRequested?.Invoke(t);
        }
    }

    [RelayCommand]
    private async Task CopyUrlAsync()
    {
        var url = SelectedTrack?.Url ?? SelectedTrack?.FilePath;
        if (string.IsNullOrEmpty(url)) return;
        try
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
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
            NullActionLogger.Error("LibraryViewModel", ex, "Failed to copy target link asset route destination to local clipboard stack.");
        }
    }
}