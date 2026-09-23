using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using NullWave.Models;
using NullWave.Services;
using NullWave.Services.SmartSorting;
using NullWave.Helpers;
using NullWave.Helpers.Logging;

namespace NullWave.ViewModels;

public partial class LibraryViewModel : ObservableObject
{
    private readonly LibraryService _library;
    private readonly LocalAIService _localAI;
    private CancellationTokenSource? _stateCts;
    private CancellationTokenSource? _aiPromptCts;
    private string? _selectedArtistFilter;
    private MediaType? _mediaTypeFilter;
    private List<Track> _currentSelection = new();

    public HashSet<MediaType> ExcludedMediaTypes { get; } = new();

    public MediaType? MediaTypeFilter
    {
        get => _mediaTypeFilter;
        set { _mediaTypeFilter = value; Refresh(); }
    }

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMultiSelection))]
    [NotifyPropertyChangedFor(nameof(SelectionCountLabel))]
    private int _selectionCount;

    public bool HasMultiSelection => SelectionCount > 1;
    public string SelectionCountLabel => $"{SelectionCount} selected";

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

    public bool HasSearchQuery => !string.IsNullOrEmpty(SearchQuery) || !string.IsNullOrEmpty(_selectedArtistFilter);

    public bool IsSortedByTitle => CurrentSort == SortField.Title;
    public bool IsSortedByArtist => CurrentSort == SortField.Artist;
    public bool IsSortedBySource => CurrentSort == SortField.Source;
    public bool IsSortedByPlayCount => CurrentSort == SortField.PlayCount;
    public bool IsSortedByDate => CurrentSort == SortField.DateAdded;

    public string ResultCountLabel => Tracks.Count == 1 ? "1 track" : $"{Tracks.Count} tracks";

    public ObservableCollection<ArtistGroup> ArtistGroups { get; } = new();
    
    public event Action? NavigateToLibraryRequested;
    public event Action<Track>? TrackDetailRequested;
    public event Action<Track>? PlayTrackRequested;
    public event Action<string, System.Collections.Generic.List<Track>>? AiPlaylistRequested;
    public event Action<List<Track>>? BulkAddToPlaylistRequested;
    public event Action<Track>? AddToPlaylistRequested;

    public LibraryViewModel(LibraryService library, LocalAIService localAI)
    {
        _library = library;
        _localAI = localAI;
        TriggerRefresh(debounce: false);
        RefreshArtistGroups();
    }

    public void UpdateSelection(IEnumerable<Track> selected)
    {
        _currentSelection = selected?.ToList() ?? new List<Track>();
        SelectionCount = _currentSelection.Count;
    }

    public void Refresh() => TriggerRefresh(debounce: false);

    private void TriggerRefresh(bool debounce = false)
    {
        try
        {
            _stateCts?.Cancel();
            _stateCts?.Dispose();
        }
        catch (ObjectDisposedException)
        {
            Log.Verbose("[LibraryViewModel] _stateCts already disposed, race condition handled gracefully");
        }

        _stateCts = new CancellationTokenSource();
        var token = _stateCts.Token;

        string query = SearchQuery;
        string? artistFilter = _selectedArtistFilter;
        SortField sort = CurrentSort;
        bool ascending = SortAscending;
        LibraryView view = CurrentView;
        TrackSource? filter = ActiveSourceFilter;
        MediaType? mediaTypeFilter = MediaTypeFilter;

        _ = Task.Run(async () =>
        {
            try
            {
                if (debounce) await Task.Delay(300, token);

                var (results, wasSearch) = FetchLibraryDataInternal(query, artistFilter, sort, ascending, view, filter, mediaTypeFilter);
                if (token.IsCancellationRequested) return;

                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (token.IsCancellationRequested) return;
                    Guid? previousSelectedId = SelectedTrack?.Id;
                    Tracks.ReplaceAll(results);
                    OnPropertyChanged(nameof(ResultCountLabel));

                    if (previousSelectedId.HasValue)
                    {
                        var newSelected = Tracks.FirstOrDefault(t => t.Id == previousSelectedId.Value);
                        if (newSelected != null)
                        {
                            SelectedTrack = null;
                            SelectedTrack = newSelected;
                        }
                    }

                    if (wasSearch) NullActionLogger.SearchPerformed(query ?? artistFilter ?? string.Empty, Tracks.Count, "LibraryViewModel");
                });
            }
            catch (TaskCanceledException) { }
            catch (Exception ex) { Log.Error(ex, "Failed background state synchronization inside LibraryViewModel."); }
        });
    }
}