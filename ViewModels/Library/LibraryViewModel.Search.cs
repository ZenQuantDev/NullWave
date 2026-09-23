using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using NullWave.Models;
using NullWave.Services;
using NullWave.Helpers.Logging;

namespace NullWave.ViewModels;

public partial class LibraryViewModel
{
    public void RefreshArtistGroups()
    {
        var buckets = new Dictionary<string, (string DisplayName, HashSet<Guid> TrackIds)>();
        foreach (var track in _library.GetAll())
        {
            if (string.IsNullOrWhiteSpace(track.Artist) || track.Artist == "Unknown") continue;
            foreach (var name in LibraryService.SplitArtistCredits(track.Artist))
            {
                var key = LibraryService.NormalizeArtistKey(name);
                if (!buckets.TryGetValue(key, out var bucket))
                {
                    bucket = (name, new HashSet<Guid>());
                    buckets[key] = bucket;
                }
                bucket.TrackIds.Add(track.Id);
            }
        }

        var groups = buckets.Values.Select(b => new ArtistGroup(b.DisplayName, b.TrackIds.Count)).OrderBy(g => g.Name).ToList();
        ArtistGroups.Clear();
        foreach (var g in groups) ArtistGroups.Add(g);
    }

    [RelayCommand]
    private void SelectArtist(ArtistGroup? group)
    {
        if (group == null) return;
        _selectedArtistFilter = group.Name;
        SearchQuery = string.Empty;
        NavigateToLibraryRequested?.Invoke();
        TriggerRefresh(debounce: false);
    }

    partial void OnSearchQueryChanged(string value)
    {
        _selectedArtistFilter = null;
        try { _aiPromptCts?.Cancel(); _aiPromptCts?.Dispose(); }
        catch (ObjectDisposedException) { Log.Verbose("[LibraryViewModel] CTS already disposed"); }

        if (value.StartsWith("ai:", StringComparison.OrdinalIgnoreCase))
        {
            _aiPromptCts = new CancellationTokenSource();
            var token = _aiPromptCts.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(1500, token);
                    if (!token.IsCancellationRequested) await HandleAIPromptAsync(value);
                }
                catch (TaskCanceledException) { }
            });
        }
        else TriggerRefresh(debounce: true);
    }

    partial void OnCurrentSortChanged(SortField value) => TriggerRefresh(debounce: false);
    partial void OnSortAscendingChanged(bool value) => TriggerRefresh(debounce: false);
    partial void OnCurrentViewChanged(LibraryView value) => TriggerRefresh(debounce: false);
    partial void OnActiveSourceFilterChanged(TrackSource? value) => TriggerRefresh(debounce: false);

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
                "is" => (value == "favorite" || value == "fav") ? (Func<Track, bool>)(t => t.IsFavorite) : t => true,
                _ => t => true
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
                FlushPending();
                pendingKey = word[..colonIndex];
                var firstValuePart = word[(colonIndex + 1)..];
                if (!string.IsNullOrEmpty(firstValuePart)) pendingValueParts.Add(firstValuePart);
            }
            else if (pendingKey != null) pendingValueParts.Add(word);
            else if (word.StartsWith('-'))
            {
                var excluded = word[1..].ToLowerInvariant();
                filters.Add(t => !t.Title.ToLowerInvariant().Contains(excluded) && !t.Artist.ToLowerInvariant().Contains(excluded));
            }
            else globalTerms.Add(word.ToLowerInvariant());
        }
        FlushPending();

        return tracks.Where(t =>
        {
            if (!filters.All(f => f(t))) return false;
            if (globalTerms.Count > 0) return globalTerms.Any(term => t.Title.ToLowerInvariant().Contains(term) || t.Artist.ToLowerInvariant().Contains(term));
            return true;
        });
    }

    private (IEnumerable<Track> Results, bool WasSearchExecuted) FetchLibraryDataInternal(
        string? query, string? artistFilter, SortField sort, bool ascending, LibraryView view, TrackSource? filter, MediaType? mediaTypeFilter)
    {
        IEnumerable<Track> baseSet = view switch
        {
            LibraryView.Favorites => _library.GetFavorites(),
            LibraryView.Recent    => _library.GetRecentlyAdded(),
            LibraryView.Source    => filter.HasValue ? _library.FilterBySource(filter.Value) : _library.GetAll(),
            _                     => _library.GetAll()
        };

        if (!string.IsNullOrEmpty(artistFilter))
        {
            var normalizedFilter = LibraryService.NormalizeArtistKey(artistFilter);
            baseSet = baseSet.Where(t => LibraryService.SplitArtistCredits(t.Artist).Any(name => LibraryService.NormalizeArtistKey(name) == normalizedFilter));
        }

        bool wasSearch = false;
        if (!string.IsNullOrWhiteSpace(query)) { baseSet = ApplySmartSearch(baseSet, query); wasSearch = true; }
        else if (!string.IsNullOrEmpty(artistFilter)) wasSearch = true;

        if (mediaTypeFilter.HasValue) baseSet = baseSet.Where(t => t.MediaType == mediaTypeFilter.Value);
        else if (ExcludedMediaTypes.Count > 0) baseSet = baseSet.Where(t => !ExcludedMediaTypes.Contains(t.MediaType));

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

    private async Task HandleAIPromptAsync(string prompt)
    {
        var query = prompt.Substring(3).Trim();
        if (string.IsNullOrWhiteSpace(query)) return;

        LiveNotification? activity = null;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            activity = ToastService.Instance.StartLiveActivity("AI Playlist Generation", "Analyzing your library...", isIndeterminate: true);
        });

        try
        {
            var allTracks = _library.GetAll().ToArray();
            var keywords = query.ToLowerInvariant().Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Where(k => k.Length > 2).ToArray();
            var candidateTracks = allTracks.Where(t =>
            {
                var searchText = $"{t.Title} {t.Artist} {string.Join(" ", t.Tags)}".ToLowerInvariant();
                return keywords.Any(k => searchText.Contains(k));
            }).ToArray();

            if (candidateTracks.Length == 0)
            {
                await Dispatcher.UIThread.InvokeAsync(() => { if (activity != null) ToastService.Instance.CompleteLiveActivity(activity, "No matching tracks found."); });
                return;
            }

            const int MaxCandidatesForAI = 60;
            Track[] finalCandidates = candidateTracks.Length > MaxCandidatesForAI
                ? candidateTracks.OrderByDescending(t => t.IsFavorite).ThenByDescending(t => t.PlayCount).Take(MaxCandidatesForAI).ToArray()
                : candidateTracks;

            await Dispatcher.UIThread.InvokeAsync(() => { if (activity != null) ToastService.Instance.UpdateLiveActivity(activity, message: $"Found {finalCandidates.Length} candidates. Asking AI to rank..."); });

            var rankedIds = await _localAI.RankTracksForMoodAsync(query, "custom", 20.0, finalCandidates, maxResults: Math.Min(50, finalCandidates.Length));
            var rankedTracks = rankedIds.Select(id => allTracks.FirstOrDefault(t => t.Id.ToString() == id)).Where(t => t != null).Cast<Track>().ToList();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                AiPlaylistRequested?.Invoke(query, rankedTracks);
                if (activity != null)
                {
                    ToastService.Instance.CompleteLiveActivity(activity, $"AI playlist '{query}' created with {rankedTracks.Count} tracks!", lingerMs: 6000,
                        actionText: "Play Now", actionCallback: () => { if (rankedTracks.Count > 0) PlayTrackRequested?.Invoke(rankedTracks[0]); });
                }
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "AI playlist generation failed");
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (activity != null) ToastService.Instance.CompleteLiveActivity(activity, "AI generation failed.");
                ToastService.Instance.Show("AI playlist generation failed.", ToastType.Error);
            });
        }
    }
}