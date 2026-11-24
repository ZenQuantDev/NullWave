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
using NullWave.Services.Security;
using RelayCommand = CommunityToolkit.Mvvm.Input.RelayCommand;

namespace NullWave.ViewModels;

public partial class LibraryViewModel : ObservableObject
{
    private readonly LibraryService _library;
    private readonly LocalAIService _localAI;
    private readonly PreferencesService _prefs;
    private readonly IdentityService _identity;  
    private CancellationTokenSource? _stateCts;
    private CancellationTokenSource? _aiPromptCts;
    private string? _selectedArtistFilter;
    private bool _isProgrammaticClear;
    private MediaType? _mediaTypeFilter;
    private List<Track> _currentSelection = new();

    // last values used by the previous TriggerRefresh, so we can tell a filter/view/source
    // SWITCH (fade the list) from a SEARCH keystroke or a SORT change (stay instant).
    private string _lastQuery = string.Empty;
    private string? _lastArtistFilter;
    private LibraryView _lastView = LibraryView.All;
    private TrackSource? _lastSourceFilter;
    private MediaType? _lastMediaTypeFilter;
    private bool _listSwitchPending;

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
    [NotifyPropertyChangedFor(nameof(IsSortedByLength))]
    private SortField _currentSort = SortField.DateAdded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSortedByTitle))]
    [NotifyPropertyChangedFor(nameof(IsSortedByArtist))]
    [NotifyPropertyChangedFor(nameof(IsSortedBySource))]
    [NotifyPropertyChangedFor(nameof(IsSortedByPlayCount))]
    [NotifyPropertyChangedFor(nameof(IsSortedByDate))]
    [NotifyPropertyChangedFor(nameof(IsSortedByLength))]
    private bool _sortAscending = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFavoritesView))]
    [NotifyPropertyChangedFor(nameof(IsRecentView))]
    private LibraryView _currentView = LibraryView.All;

    [ObservableProperty]
    private TrackSource? _activeSourceFilter = null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMultiSelection))]
    [NotifyPropertyChangedFor(nameof(SelectionCountLabel))]
    private int _selectionCount;

    // ---- MATCH dip-dissolve state. The view drives the two legs; the VM defers the
    // data swap to the trough so OLD fades out (visible) before NEW fades in (no blink).
    // Invariant: a SWITCH bumps _switchSeq; an INSTANT refresh does NOT bump it, it only
    // clears flags+pending. The view's scheduled callbacks compare their captured seq to
    // the current seq to tell "superseded by a newer fade" (bail silently) from
    // "aborted by an instant refresh" (snap visible). ----
    public int ListSwitchSeq => _switchSeq;
    private int _switchSeq;
    private IList<Track>? _pendingSwitchList;
    private Guid? _pendingPreviousSelectedId;
    private bool _pendingWasSearch;
    private string? _pendingSearchLog;

    [ObservableProperty] private bool _isListFadingOut; // rising edge -> view fades CURRENT list 1->0
    [ObservableProperty] private bool _isListFadingIn;  // rising edge -> view fades NEW list 0->1

    public bool HasMultiSelection => SelectionCount > 1;
    public string SelectionCountLabel => $"{SelectionCount} selected";

    public enum LibraryView { All, Favorites, Recent, Source }

    public BulkObservableCollection<Track> Tracks { get; } = new();
    public ObservableCollection<SourceFilterViewModel> SourceFilters { get; } = new();

    /// <summary>Only the sorts that make sense outside a playlist (never "Custom").</summary>
    public Array SortOptions => SortFieldCatalog.Library;
    
    public bool IsFavoritesView => CurrentView == LibraryView.Favorites;
    public bool IsRecentView => CurrentView == LibraryView.Recent;

    public bool HasSearchQuery => !string.IsNullOrEmpty(SearchQuery) || !string.IsNullOrEmpty(_selectedArtistFilter);

    public bool IsSortedByTitle => CurrentSort == SortField.Title;
    public bool IsSortedByArtist => CurrentSort == SortField.Artist;
    public bool IsSortedBySource => CurrentSort == SortField.Source;
    public bool IsSortedByPlayCount => CurrentSort == SortField.PlayCount;
    public bool IsSortedByDate => CurrentSort == SortField.DateAdded;
    public bool IsSortedByLength => CurrentSort == SortField.Length;

    public string ResultCountLabel => Tracks.Count == 1 ? "1 track" : $"{Tracks.Count} tracks";
    /// <summary>Only the main Library VM should trigger the AI playlist generation prompt.</summary>
    public bool IsAiPromptEnabled { get; set; }

    /// <summary>2.5: Callback to capture which playlists a track belongs to before deletion.</summary>
    public Func<Guid, List<Guid>>? GetPlaylistMemberships { get; set; }
    
    /// <summary>2.5: Callback to restore a track to its playlists after an Undo.</summary>
    public Action<Track, List<Guid>>? RestorePlaylistMemberships { get; set; }

    public ObservableCollection<ArtistGroup> ArtistGroups { get; } = new();
    
    public event Action? NavigateToLibraryRequested;
    public event Action<Track>? TrackDetailRequested;
    public event Action<Track>? PlayTrackRequested;
    public event Action<string, System.Collections.Generic.List<Track>>? AiPlaylistRequested;
    public event Action<List<Track>>? BulkAddToPlaylistRequested;
    public event Action<Track>? AddToPlaylistRequested;

    public LibraryViewModel(LibraryService library, LocalAIService localAI, PreferencesService prefs, IdentityService identity)
    {
        _library = library;
        _localAI = localAI;
        _prefs = prefs;
        _identity = identity; 
        TriggerRefresh(debounce: false);
        RefreshArtistGroups();
        BuildSourceFilters();
        UpdateSourceCounts();
    }

    private void BuildSourceFilters()
    {
        SourceFilters.Clear();

        var alwaysShow = new[] { TrackSource.YouTube, TrackSource.SoundCloud, TrackSource.Local };

        foreach (var source in alwaysShow)
        {
            var site = SupportedSites.FindBySource(source);
            if (site == null) continue;

            SourceFilters.Add(new SourceFilterViewModel(
                site.DisplayName,
                site.Source,
                site.IconKind,
                site.IconColorHex,
                new RelayCommand(() => SetSourceFilter(source))
            ));
        }
    }

    public void UpdateSourceCounts()
    {
        var counts = _library.GetSourceCounts();

        foreach (var filter in SourceFilters)
        {
            filter.TrackCount = counts.GetValueOrDefault(filter.Source, 0);
        }

        foreach (var (source, count) in counts)
        {
            if (count == 0) continue;
            if (SourceFilters.Any(filter => filter.Source == source)) continue;

            var site = SupportedSites.FindBySource(source);
            if (site == null) continue;

            SourceFilters.Add(new SourceFilterViewModel(
                site.DisplayName,
                site.Source,
                site.IconKind,
                site.IconColorHex,
                new RelayCommand(() => SetSourceFilter(source))
            ) { TrackCount = count });
        }
    }

    public void UpdateSelection(IEnumerable<Track> selected)
    {
        _currentSelection = selected?.ToList() ?? new List<Track>();
        SelectionCount = _currentSelection.Count;
    }

    public void Refresh() => TriggerRefresh(debounce: false);

        /// <summary>Called by TrackListView when the OLD list has finished fading out (the trough).
    /// Applies the deferred swap here — while opacity is 0 — then raises the fade-IN leg.</summary>
    public void CompleteListFadeOut(int seq)
    {
        if (seq != _switchSeq || _pendingSwitchList is null) return; // superseded or already drained
        var list = _pendingSwitchList;
        _pendingSwitchList = null;
        Tracks.ReplaceAll(list);
        UpdateSourceCounts();
        OnPropertyChanged(nameof(ResultCountLabel));
        if (_pendingPreviousSelectedId.HasValue)
        {
            var ns = Tracks.FirstOrDefault(t => t.Id == _pendingPreviousSelectedId.Value);
            if (ns != null) { SelectedTrack = null; SelectedTrack = ns; }
        }
        if (_pendingWasSearch) NullActionLogger.SearchPerformed(_pendingSearchLog ?? string.Empty, Tracks.Count, "LibraryViewModel");
        _pendingPreviousSelectedId = null; _pendingWasSearch = false; _pendingSearchLog = null;
        IsListFadingOut = false;
        IsListFadingIn = true;   // view fades NEW in
    }

    /// <summary>Called by TrackListView when the NEW list has finished fading in. Clears the flag
    /// so the next switch re-arms. Data was already applied at the trough.</summary>
    public void CompleteListFadeIn(int seq)
    {
        if (seq != _switchSeq) return; // superseded by a newer dip
        IsListFadingIn = false;
    }

    /// <summary>An instant refresh (search keystroke / sort / programmatic) that lands while a dip
    /// is in flight must discard the pending swap and clear the flags WITHOUT bumping the seq,
    /// so the view's scheduled callback can tell "aborted" (snap visible) from "superseded" (bail).</summary>
    private void AbortInFlightFadeIfNeeded()
    {
        if (!IsListFadingOut && !IsListFadingIn && _pendingSwitchList is null) return;
        _pendingSwitchList = null;
        _pendingPreviousSelectedId = null; _pendingWasSearch = false; _pendingSearchLog = null;
        IsListFadingOut = false;
        IsListFadingIn = false;
        // deliberately no _switchSeq++ : the in-flight view callback sees seq==current + flags cleared -> snaps.
    }

    /// <summary>Heal a dip that was orphaned because the view detached mid-animation (e.g. the user
    /// navigated Library->Radio during a fade). Applies any pending swap instantly, bumps the seq so
    /// the orphaned view's scheduled callbacks bail, and clears the flags. Called on (re)attach.</summary>
    public void ForceResolvePendingFade()
    {
        if (_pendingSwitchList != null)
        {
            var list = _pendingSwitchList; _pendingSwitchList = null;
            Tracks.ReplaceAll(list);
            UpdateSourceCounts();
            OnPropertyChanged(nameof(ResultCountLabel));
        }
        _pendingPreviousSelectedId = null; _pendingWasSearch = false; _pendingSearchLog = null;
        _switchSeq++;            // invalidate any in-flight view callbacks
        IsListFadingOut = false;
        IsListFadingIn = false;
    }

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

        // A "switch" = view/source/mediatype/artist changed. Search text and sort direction
        // deliberately do NOT count, so typing and re‑sorting stay instant.
        _listSwitchPending =
            view != _lastView ||
            filter != _lastSourceFilter ||
            mediaTypeFilter != _lastMediaTypeFilter ||
            artistFilter != _lastArtistFilter;

        _lastQuery = query;
        _lastArtistFilter = artistFilter;
        _lastView = view;
        _lastSourceFilter = filter;
        _lastMediaTypeFilter = mediaTypeFilter;

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

                    var list = results as IList<Track> ?? results.ToList();
                    bool same = Tracks.Count == list.Count;
                    for (int i = 0; same && i < list.Count; i++)
                        same = ReferenceEquals(Tracks[i], list[i]);
                    if (same)
                    {
                        OnPropertyChanged(nameof(ResultCountLabel));
                        return;
                    }

                    if (_listSwitchPending)
                    {
                        // Defer the swap to the trough: keep OLD rendered so it can fade out.
                        _listSwitchPending = false;
                        _switchSeq++;
                        _pendingSwitchList = list as IList<Track> ?? list?.ToList();
                        _pendingPreviousSelectedId = SelectedTrack?.Id;
                        _pendingWasSearch = wasSearch;
                        _pendingSearchLog = wasSearch ? (query ?? artistFilter ?? string.Empty) : null;
                        IsListFadingOut = true;   // view fades OLD out, then calls CompleteListFadeOut
                        OnPropertyChanged(nameof(ListSwitchSeq)); // supersede signal: fires even when IsListFadingOut was already true
                        return;                  // do NOT touch Tracks here
                    }

                    AbortInFlightFadeIfNeeded();  // a search/sort landing mid-dip must not leave opacity < 1

                    Guid? previousSelectedId = SelectedTrack?.Id;
                    Tracks.ReplaceAll(list);
                    UpdateSourceCounts();
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