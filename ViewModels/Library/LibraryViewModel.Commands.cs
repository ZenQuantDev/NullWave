using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using NullWave.Models;
using NullWave.Services;
using NullWave.Helpers;
using NullWave.Helpers.Logging;
using NullWave.Services.Security;

namespace NullWave.ViewModels;

public partial class LibraryViewModel
{
    private void SetSort(SortField field)
    {
        if (CurrentSort == field) SortAscending = !SortAscending;
        else { CurrentSort = field; SortAscending = true; }
    }

    private void SetSourceFilter(TrackSource source)
    {
        if (ActiveSourceFilter == source) { CurrentView = LibraryView.All; ActiveSourceFilter = null; }
        else { CurrentView = LibraryView.Source; ActiveSourceFilter = source; }
    }

    public void ShowAll() { CurrentView = LibraryView.All; ActiveSourceFilter = null; }

    [RelayCommand]
    private async Task RemoveTrackAsync(Track? t)
    {
        var target = t ?? SelectedTrack;
        if (target == null) return;
        Guid targetId = target.Id;
        if (SelectedTrack?.Id == targetId) SelectedTrack = null;
        var undoTrack = target;

        await Task.Run(() => _library.Remove(targetId));
        NullActionLogger.TrackRemoved(targetId.ToString(), "LibraryViewModel");
        TriggerRefresh(debounce: false);

        ToastService.Instance.Show(message: $"Removed '{undoTrack.Title}'", type: ToastType.Warning, durationMs: 6000, actionText: "Undo",
            actionCallback: () => { _library.Add(undoTrack); TriggerRefresh(debounce: false); ToastService.Instance.Show("Track restored.", ToastType.Success, durationMs: 2000, scope: "library-delete"); },
            scope: "library-delete");
    }

    [RelayCommand]
    private async Task BulkRemoveAsync()
    {
        var targets = _currentSelection.ToList();
        if (targets.Count == 0) return;
        await Task.Run(() => { foreach (var t in targets) _library.Remove(t.Id); });
        NullActionLogger.User("BulkRemove", $"count={targets.Count}", "LibraryViewModel");
        TriggerRefresh(debounce: false);

        ToastService.Instance.Show(message: $"Removed {targets.Count} track(s)", type: ToastType.Warning, durationMs: 6000, actionText: "Undo",
            actionCallback: () => { foreach (var t in targets) _library.Add(t); TriggerRefresh(debounce: false); ToastService.Instance.Show("Tracks restored.", ToastType.Success, durationMs: 2000, scope: "library-delete"); },
            scope: "library-delete");
    }

    [RelayCommand]
    private void BulkAddToQueue()
    {
        var targets = _currentSelection.ToList();
        if (targets.Count == 0) return;
        foreach (var t in targets) _library.AddToQueue(t.Id);
        NullActionLogger.User("BulkAddToQueue", $"count={targets.Count}", "LibraryViewModel");
        ToastService.Instance.Show(message: $"Added {targets.Count} track(s) to queue", type: ToastType.Info, durationMs: 2500, scope: "queue-add");
    }

    [RelayCommand]
    private async Task BulkToggleFavoriteAsync()
    {
        var targets = _currentSelection.ToList();
        if (targets.Count == 0) return;
        bool desired = targets.Any(t => !t.IsFavorite);
        await Task.Run(() => { foreach (var t in targets) { if (t.IsFavorite != desired) _library.ToggleFavorite(t.Id); } });
        NullActionLogger.User("BulkToggleFavorite", $"count={targets.Count} fav={desired}", "LibraryViewModel");
        TriggerRefresh(debounce: false);
    }

    [RelayCommand] private void BulkAddToPlaylist() { var targets = _currentSelection.ToList(); if (targets.Count > 0) BulkAddToPlaylistRequested?.Invoke(targets); }

    [RelayCommand]
    private void MarkAsAudiobook()
    {
        var targets = _currentSelection.Any() ? _currentSelection : (SelectedTrack != null ? new List<Track> { SelectedTrack } : new List<Track>());
        if (!targets.Any()) return;
        foreach (var t in targets) { t.MediaType = MediaType.Audiobook; _library.Update(t); }
        TriggerRefresh(debounce: false);
        ToastService.Instance.Show($"Marked {targets.Count} item(s) as Audiobook.", ToastType.Success, 2000);
    }

    [RelayCommand]
    private void MarkAsMusic()
    {
        var targets = _currentSelection.Any() ? _currentSelection : (SelectedTrack != null ? new List<Track> { SelectedTrack } : new List<Track>());
        if (!targets.Any()) return;
        foreach (var t in targets) { t.MediaType = MediaType.Music; _library.Update(t); }
        TriggerRefresh(debounce: false);
        ToastService.Instance.Show($"Marked {targets.Count} item(s) as Music.", ToastType.Success, 2000);
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync(Track? t)
    {
        var target = t ?? SelectedTrack;
        if (target == null) return;
        Guid targetId = target.Id;
        bool expectedNewState = !target.IsFavorite;
        await Task.Run(() => _library.ToggleFavorite(targetId));
        NullActionLogger.FavoriteToggled(targetId.ToString(), expectedNewState, "LibraryViewModel");
        TriggerRefresh(debounce: false);
    }

    [RelayCommand]
    private async Task RecordPlayAsync()
    {
        if (SelectedTrack == null) return;
        await Task.Run(() => _library.RecordPlay(SelectedTrack.Id));
        TriggerRefresh(debounce: false);
    }

    [RelayCommand]
    private void AddToQueue(Track? t)
    {
        var target = t ?? SelectedTrack;
        if (target == null) return;
        _library.AddToQueue(target.Id);
        NullActionLogger.User("AddToQueue", target.Id.ToString(), "LibraryViewModel");
        ToastService.Instance.Show(message: $"Added '{target.Title}' to queue", type: ToastType.Info, durationMs: 2500, scope: "queue-add");
    }

    [RelayCommand] private void RequestAddToPlaylist(Track? t) { var target = t ?? SelectedTrack; if (target != null) AddToPlaylistRequested?.Invoke(target); }

    [RelayCommand] private void SortByTitle() => SetSort(SortField.Title);
    [RelayCommand] private void SortByArtist() => SetSort(SortField.Artist);
    [RelayCommand] private void SortByDate() => SetSort(SortField.DateAdded);
    [RelayCommand] private void SortByPlayCount() => SetSort(SortField.PlayCount);
    [RelayCommand] private void SortBySource() => SetSort(SortField.Source);
    [RelayCommand] private void SortByLastPlayed() => SetSort(SortField.LastPlayed);
    [RelayCommand] private void FocusSearch() => SearchQuery = string.Empty;
    [RelayCommand] private void ClearSearchText() => SearchQuery = string.Empty;
    [RelayCommand] private void ToggleSortDirection() => SortAscending = !SortAscending;
    [RelayCommand] private void ClearSearch() { SearchQuery = string.Empty; _selectedArtistFilter = null; CurrentView = LibraryView.All; ActiveSourceFilter = null; }
    [RelayCommand] private void ShowFavorites() { CurrentView = LibraryView.Favorites; ActiveSourceFilter = null; }
    [RelayCommand] private void ShowRecent() { CurrentView = LibraryView.Recent; ActiveSourceFilter = null; }
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
        if (SelectedTrack == null) return;
        if (await ClipboardHelper.CopyTrackLinkAsync(SelectedTrack, _prefs, _identity))
            ToastService.Instance.Show("URL copied to clipboard.", ToastType.Success, durationMs: 2000);
    }
}