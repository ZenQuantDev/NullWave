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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSearchQuery))]
    [NotifyPropertyChangedFor(nameof(IsSortedByTitle))]
    [NotifyPropertyChangedFor(nameof(IsSortedByArtist))]
    [NotifyPropertyChangedFor(nameof(IsSortedBySource))]
    [NotifyPropertyChangedFor(nameof(IsSortedByPlayCount))]
    [NotifyPropertyChangedFor(nameof(IsSortedByDate))]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSortedByTitle))]
    [NotifyPropertyChangedFor(nameof(IsSortedByArtist))]
    [NotifyPropertyChangedFor(nameof(IsSortedBySource))]
    [NotifyPropertyChangedFor(nameof(IsSortedByPlayCount))]
    [NotifyPropertyChangedFor(nameof(IsSortedByDate))]
    private Track? _selectedTrack;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSortedByTitle))]
    [NotifyPropertyChangedFor(nameof(IsSortedByArtist))]
    [NotifyPropertyChangedFor(nameof(IsSortedBySource))]
    [NotifyPropertyChangedFor(nameof(IsSortedByPlayCount))]
    [NotifyPropertyChangedFor(nameof(IsSortedByDate))]
    private SortField _currentSort = SortField.DateAdded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSortedByTitle))]
    [NotifyPropertyChangedFor(nameof(IsSortedByArtist))]
    [NotifyPropertyChangedFor(nameof(IsSortedBySource))]
    [NotifyPropertyChangedFor(nameof(IsSortedByPlayCount))]
    [NotifyPropertyChangedFor(nameof(IsSortedByDate))]
    private bool _sortAscending = true;

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
    
    public bool HasSearchQuery => !string.IsNullOrEmpty(SearchQuery);
    
    public bool IsSortedByTitle => CurrentSort == SortField.Title;
    public bool IsSortedByArtist => CurrentSort == SortField.Artist;
    public bool IsSortedBySource => CurrentSort == SortField.Source;
    public bool IsSortedByPlayCount => CurrentSort == SortField.PlayCount;
    public bool IsSortedByDate => CurrentSort == SortField.DateAdded;
    
    public string ResultCountLabel => Tracks.Count == 1 ? "1 track" : $"{Tracks.Count} tracks";

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
                    OnPropertyChanged(nameof(ResultCountLabel));

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

    private void SetSort(SortField field)
    {
        if (CurrentSort == field) SortAscending = !SortAscending;
        else { CurrentSort = field; SortAscending = true; }
    }

    private IEnumerable<Track> ApplySmartSearch(IEnumerable<Track> tracks, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return tracks;

        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var filters = new List<Func<Track, bool>>();
        var globalTerms = new List<string>();

        string? pendingKey = null;
        var pendingValueParts = new List<string>();

        void FlushPending()
        {
            if (pendingKey == null) return;
            var value = string.Join(' ', pendingValueParts).ToLowerInvariant();
            var negate = pendingKey.StartsWith('-');
            var key = negate ? pendingKey[1..] : pendingKey;

            Func<Track, bool> filter = key switch
            {
                "artist" or "a" => t => t.Artist.ToLowerInvariant().Contains(value),
                "title" or "t"  => t => t.Title.ToLowerInvariant().Contains(value),
                "source" or "s" => t => t.Source.ToString().ToLowerInvariant().Contains(value),
                "tag" or "genre" => t => t.Tags.Any(tag => tag.ToLowerInvariant().Contains(value)),
                "is" => (value == "favorite" || value == "fav")
                    ? (Func<Track, bool>)(t => t.IsFavorite)
                    : t => true,
                _ => t => true // unknown key, ignore rather than break the whole query
            };

            filters.Add(negate ? (t => !filter(t)) : filter);
            pendingKey = null;
            pendingValueParts.Clear();
        }

        foreach (var word in words)
        {
            var colonIndex = word.IndexOf(':');
            if (colonIndex > 0)
            {
                FlushPending(); // a new key: starts — close out whatever we were accumulating
                pendingKey = word[..colonIndex];
                var firstValuePart = word[(colonIndex + 1)..];
                if (!string.IsNullOrEmpty(firstValuePart))
                    pendingValueParts.Add(firstValuePart);
            }
            else if (pendingKey != null)
            {
                pendingValueParts.Add(word); // still part of the previous key's value
            }
            else if (word.StartsWith('-'))
            {
                // bare "-word" excludes tracks matching that word in Title/Artist
                var excluded = word[1..].ToLowerInvariant();
                filters.Add(t => !t.Title.ToLowerInvariant().Contains(excluded)
                               && !t.Artist.ToLowerInvariant().Contains(excluded));
            }
            else
            {
                globalTerms.Add(word.ToLowerInvariant());
            }
        }
        FlushPending();

        return tracks.Where(t =>
        {
            if (!filters.All(f => f(t))) return false;

            if (globalTerms.Count > 0)
            {
                return globalTerms.Any(term =>
                    t.Title.ToLowerInvariant().Contains(term) ||
                    t.Artist.ToLowerInvariant().Contains(term));
            }

            return true;
        });
    }

    private (IEnumerable<Track> Results, bool WasSearchExecuted) FetchLibraryDataInternal(
        string query, SortField sort, bool ascending, LibraryView view, TrackSource? filter)
    {
        // Step 1: pick the base candidate set for the active view/filter.
        IEnumerable<Track> baseSet = view switch
        {
            LibraryView.Favorites => _library.GetFavorites(),
            LibraryView.Recent    => _library.GetRecentlyAdded(),
            LibraryView.Source    => filter.HasValue ? _library.FilterBySource(filter.Value) : _library.GetAll(),
            _                     => _library.GetAll()
        };

        // Step 2: Apply Smart Search (handles both global text and key:value filters)
        bool wasSearch = false;
        if (!string.IsNullOrWhiteSpace(query))
        {
            baseSet = ApplySmartSearch(baseSet, query);
            wasSearch = true;
        }

        // Step 3: sort always applies too, with secondary tie-breakers to stabilize groups
        IEnumerable<Track> sorted = sort switch
        {
            SortField.Title      => baseSet.OrderBy(t => t.Title).ThenBy(t => t.Artist),
            SortField.Artist     => baseSet.OrderBy(t => t.Artist).ThenBy(t => t.Title),
            SortField.DateAdded  => baseSet.OrderBy(t => t.DateAdded).ThenBy(t => t.Title),
            SortField.Source     => baseSet.OrderBy(t => t.Source).ThenBy(t => t.Title),
            SortField.PlayCount  => baseSet.OrderBy(t => t.PlayCount).ThenBy(t => t.Title),
            SortField.LastPlayed => baseSet.OrderBy(t => t.LastPlayed).ThenBy(t => t.Title),
            _ => baseSet
        };

        return ((ascending ? sorted : sorted.Reverse()).ToList(), wasSearch);
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

    [RelayCommand] private void SortByTitle() => SetSort(SortField.Title);
    [RelayCommand] private void SortByArtist() => SetSort(SortField.Artist);
    [RelayCommand] private void SortByDate() => SetSort(SortField.DateAdded);
    [RelayCommand] private void SortByPlayCount() => SetSort(SortField.PlayCount);
    [RelayCommand] private void SortBySource() => SetSort(SortField.Source);
    [RelayCommand] private void SortByLastPlayed() => SetSort(SortField.LastPlayed);
    [RelayCommand] private void FocusSearch() => SearchQuery = string.Empty;
    [RelayCommand] private void ClearSearchText() => SearchQuery = string.Empty;
    [RelayCommand] private void ToggleSortDirection() => SortAscending = !SortAscending;

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