using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Serilog;
using NullWave.Models;
using NullWave.Services;
using NullWave.Helpers.Logging;

namespace NullWave.ViewModels;

public partial class PlayerViewModel
{
    private bool _queueInterrupt;
    private System.Collections.Generic.List<Guid> _playlistDeck = new();
    private int _playlistDeckPos;

    public void PlayPlaylist(Playlist playlist)
    {
        if (playlist.Tracks.Count == 0) return;
        _activePlaylist = playlist;
        _activePlaylistIndex = 0;
        _playlistDeck.Clear();
        _playlistDeckPos = 0;
        PlayTrack(playlist.Tracks[0]);
    }

    public void PlayTrack(Track? track)
    {
        if (track == null) return;
        NowPlayingTitle = string.Empty;
        NowPlayingArtist = string.Empty;

        var hadPlaylist = _activePlaylist != null;

        if (_activePlaylist != null)
        {
            int idx = -1;
            for (int i = 0; i < _activePlaylist.Tracks.Count; i++) { if (_activePlaylist.Tracks[i].Id == track.Id) { idx = i; break; } }
            if (idx >= 0) _activePlaylistIndex = idx; else if (!_queueInterrupt) _activePlaylist = null;
        }
        _queueInterrupt = false;

        // Keep the queue panel truthful: rebuild auto "up next" whenever the context or index moves.
        if (_activePlaylist != null) RebuildAutoQueue();
        else if (hadPlaylist) { _library.ClearAutoQueue(); RefillAutoQueue(); }

        var recordHistory = !_suppressHistoryRecord;
        _suppressHistoryRecord = false;

        if (_currentTrack != null && _currentTrack.Id != track.Id)
        {
            SaveAudiobookProgress();
            if (recordHistory) _navigator.RecordPlay(_currentTrack);
        }

        _hasTriggeredCrossfade = false;

        if (_currentTrack != null && track.Id == _currentTrack.Id && _state == PlaybackState.Playing)
        {
            ShowAlreadyPlayingToast = true;
            _ = Task.Run(async () => { await Task.Delay(1500); Dispatcher.UIThread.Post(() => ShowAlreadyPlayingToast = false); });
            return;
        }

        var contextChanged = _currentTrack == null || _currentTrack.MediaType != track.MediaType || (track.MediaType == MediaType.Audiobook && (_currentTrack.Album != track.Album || _currentTrack.Artist != track.Artist));
        _playSessionId = Guid.NewGuid();
        CurrentTrack = track;
        _navigator.CurrentTrack = track;
        if (contextChanged) _navigator.InvalidateDeck();
        AlbumArtPath = track.AlbumArtPath;
        _playRecorded = false;

        if (track.MediaType == MediaType.Radio && !string.IsNullOrEmpty(track.Url))
        {
            _pendingResumeTicks = 0;
            _playback.SetRate(1.0f);
            _playback.Play(track.Url);
            _trackStartTime = DateTime.UtcNow;
            StatusText = CurrentTrackDisplay;
            NullActionLogger.TrackPlayed(track.Id.ToString(), track.Title, track.Artist, nameof(PlayerViewModel));
            return;
        }

        if (!string.IsNullOrEmpty(track.FilePath) && File.Exists(track.FilePath))
        {
            _pendingResumeTicks = track.MediaType == MediaType.Audiobook && _settings.AutoResumeAudiobooks ? track.PlaybackPositionTicks : 0;
            _playback.Play(track.FilePath);
            if (track.MediaType == MediaType.Audiobook) _playback.SetRate(_playbackRate); else _playback.SetRate(1.0f);
            if (track.MediaType == MediaType.Audiobook) { _audiobookSaveTimer?.Dispose(); _audiobookSaveTimer = new System.Threading.Timer(_ => SaveAudiobookProgress(), null, 5000, 5000); }
            else { _audiobookSaveTimer?.Dispose(); _audiobookSaveTimer = null; }
            _trackStartTime = DateTime.UtcNow;
            StatusText = CurrentTrackDisplay;
            NullActionLogger.TrackPlayed(track.Id.ToString(), track.Title, track.Artist, nameof(PlayerViewModel));
            return;
        }

        if (!string.IsNullOrEmpty(track.Url))
        {
            if (!IsDownloading)
            {
                IsDownloading = true;
                StatusText = L("Player_Status_DownloadingBeforePlayback");
                NullActionLogger.ImportStarted(track.Url, nameof(PlayerViewModel));
                _ = _download.DownloadAsync(track.Id.ToString(), track.Url, _settings.AudioFormat, _settings.AudioQuality,
                    title: track.Title, artist: track.Artist);
            }
            else { StatusText = L("Player_Status_DownloadInProgress"); Log.Debug("[{Source}] Skipped duplicate download for {Url}", nameof(PlayerViewModel), track.Url); }
            return;
        }

        Log.Warning("[PlayerViewModel] PlayTrack failed: FilePath='{FilePath}', Exists={Exists}, Url='{Url}'", track.FilePath, File.Exists(track.FilePath), track.Url);
        StatusText = L("Player_Status_NoPlayableSource");
    }

    private void PlayPause()
    {
        if (IsPlaying)
        {
            if (!_playback.IsPlaying && !_playback.IsCrossfading)
            {
                _playback.ResyncState();
                return;
            }
            SaveAudiobookProgress();
            if (_settings.FadeOnPauseEnabled) _ = _playback.FadeAndPauseAsync(_settings.FadeOnPauseDurationMs); else _playback.Pause();
            NullActionLogger.TrackPaused(_currentTrack?.Id.ToString() ?? "?", PositionDisplay, nameof(PlayerViewModel));
        }
        else if (_state == PlaybackState.Paused)
        {
            if (_playback.IsPlaying)
            {
                _playback.ResyncState();
                return;
            }
            if (_settings.FadeOnPauseEnabled) _ = _playback.FadeAndResumeAsync(_settings.FadeOnPauseDurationMs); else _playback.Resume();
            if (_currentTrack != null) NullActionLogger.TrackPlayed(_currentTrack.Id.ToString(), _currentTrack.Title, _currentTrack.Artist, nameof(PlayerViewModel));
        }
        else if (_currentTrack != null) PlayTrack(_currentTrack);
        else PlaySelectedTrackRequested?.Invoke();
    }

    private void Stop()
    {
        SaveAudiobookProgress();
        _audiobookSaveTimer?.Dispose();
        _audiobookSaveTimer = null;
        _playback.Stop();
        if (_currentTrack != null) NullActionLogger.TrackStopped(_currentTrack.Id.ToString(), nameof(PlayerViewModel));
    }

    private void SaveAudiobookProgress()
    {
        if (_currentTrack?.MediaType != MediaType.Audiobook) return;
        var duration = _playback.Duration;
        var currentTicks = (long)(_position * duration.Ticks);
        if (currentTicks > 0 && currentTicks != _currentTrack.PlaybackPositionTicks) { _currentTrack.PlaybackPositionTicks = currentTicks; _library.Update(_currentTrack); }
    }

    private void OnTrackFinished()
    {
        if (_isCrossfading) return;
        if (_currentTrack != null)
        {
            if (_currentTrack.MediaType == MediaType.Audiobook) { _currentTrack.PlaybackPositionTicks = 0; _library.Update(_currentTrack); }
            if (!_playRecorded) _library.RecordPlay(_currentTrack.Id);
            NullActionLogger.TrackStopped(_currentTrack.Id.ToString(), nameof(PlayerViewModel));
            if (_position >= _settings.ScrobbleThreshold && _currentTrack.MediaType != MediaType.Audiobook)
            {
                var freshTrack = _library.GetAll().FirstOrDefault(t => t.Id == _currentTrack.Id);
                TrackScrobbleRequested?.Invoke(freshTrack?.Title ?? _currentTrack.Title, freshTrack?.Artist ?? _currentTrack.Artist, DateTime.UtcNow);
            }
        }
        if (_navigator.ShouldRepeatCurrent() && _currentTrack != null) PlayTrack(_currentTrack); else PlayNext();
    }

    // Playlist-scoped no-repeat shuffle deck (mirrors PlaybackNavigator's library deck)
    private Track? DrawPlaylistShuffle()
    {
        if (_activePlaylist == null || _activePlaylist.Tracks.Count == 0) return null;
        if (_playlistDeckPos >= _playlistDeck.Count)
        {
            _playlistDeck = _activePlaylist.Tracks.Select(t => t.Id).ToList();
            for (int i = _playlistDeck.Count - 1; i > 0; i--)
            {
                int j = Random.Shared.Next(i + 1);
                (_playlistDeck[i], _playlistDeck[j]) = (_playlistDeck[j], _playlistDeck[i]);
            }
            _playlistDeckPos = 0;
            if (_currentTrack != null && _playlistDeck.Count > 1 && _playlistDeck[0] == _currentTrack.Id)
            {
                int j = Random.Shared.Next(1, _playlistDeck.Count);
                (_playlistDeck[0], _playlistDeck[j]) = (_playlistDeck[j], _playlistDeck[0]);
            }
        }
        var id = _playlistDeck[_playlistDeckPos++];
        return _activePlaylist.Tracks.FirstOrDefault(t => t.Id == id);
    }

    private void RebuildAutoQueue()
    {
        _library.ClearAutoQueue();
        RefillAutoQueue();
    }

    private void PlayPrevious()
    {
        if (DateTime.UtcNow - _lastNavigationTime < NavigationDebounce) return;
        _lastNavigationTime = DateTime.UtcNow;
        var duration = _playback.Duration.TotalSeconds;
        if (duration > 0 && Position * duration > 3.0) { SeekTo(0f); return; }
        _download.CancelCurrentDownload();
        IsDownloading = false;

        if (_currentTrack?.MediaType == MediaType.Audiobook)
        {
            var prevChapter = _navigator.GetPreviousChapter(_currentTrack);
            if (prevChapter != null) { PlayTrack(prevChapter); return; }
        }

        if (_activePlaylist != null && _currentTrack != null && _activePlaylist.Tracks.Any(t => t.Id == _currentTrack.Id))
        {
            var idx = _activePlaylist.Tracks.IndexOf(_currentTrack);
            if (idx > 0)
            {
                _activePlaylistIndex = idx - 1;
                PlayTrack(_activePlaylist.Tracks[idx - 1]);
                return;
            }
            SeekTo(0f);
            return;
        }

        if (_activePlaylist != null) _activePlaylist = null;

        var prev = _navigator.GetPreviousTrack(_currentTrack);
        if (prev != null) { _suppressHistoryRecord = true; PlayTrack(prev); }
    }

    private void PlayNext()
    {
        if (DateTime.UtcNow - _lastNavigationTime < NavigationDebounce) return;
        _lastNavigationTime = DateTime.UtcNow;
        RecordSkipIfEarly();
        _download.CancelCurrentDownload();
        IsDownloading = false;

        if (_currentTrack?.MediaType == MediaType.Audiobook)
        {
            var nextChapter = _navigator.GetNextChapter(_currentTrack);
            if (nextChapter != null) { PlayTrack(nextChapter); return; }
            StatusText = "End of Audiobook";
            return;
        }

        // 1) Queue is the single source of "up next": manual entries sit at the head,
        //    auto entries mirror the playlist (sequential or shuffle deck).
        var head = _library.GetQueue();
        if (head.Count > 0)
        {
            bool wasManual = head[0].IsManual;
            var queued = _library.DequeueNext();
            if (queued != null)
            {
                if (wasManual) _queueInterrupt = true;
                PlayTrack(queued);
                RefillAutoQueue();
                return;
            }
        }

        // 2) Queue empty (auto-fill disabled or playlist exhausted): fall back to direct picks.
        if (_activePlaylist != null && _activePlaylist.Tracks.Count > 0)
        {
            Track? pick = null;
            if (IsShuffle)
            {
                pick = DrawPlaylistShuffle();
            }
            else
            {
                var idx = _currentTrack != null ? _activePlaylist.Tracks.IndexOf(_currentTrack) : _activePlaylistIndex;
                if (idx >= 0 && idx < _activePlaylist.Tracks.Count - 1) pick = _activePlaylist.Tracks[idx + 1];
                else if (RepeatMode == RepeatMode.All)
                {
                    _playlistDeck.Clear(); _playlistDeckPos = 0;
                    pick = _activePlaylist.Tracks[0];
                }
            }
            if (pick != null) { PlayTrack(pick); RefillAutoQueue(); return; }
            StatusText = $"End of '{_activePlaylist.Name}'.";
            return;
        }

        // 3) No playlist context: original library behavior.
        var queuedAuto = _library.DequeueNext();
        if (queuedAuto != null) { PlayTrack(queuedAuto); RefillAutoQueue(); return; }

        var next = _navigator.GetNextTrack(_currentTrack);
        if (next != null) PlayTrack(next); else StatusText = L("Player_Status_EndOfLibrary");
    }

    private void RecordSkipIfEarly()
    {
        if (_currentTrack?.MediaType is MediaType.Radio or MediaType.Audiobook) return;
        if (_currentTrack == null || _trackStartTime == DateTime.MinValue) return;
        var elapsed = (DateTime.UtcNow - _trackStartTime).TotalSeconds;
        var window = _settings.SkipPenaltyWindowSeconds;
        if (elapsed < 0.5) return;
        if (elapsed <= window)
        {
            if (_currentTrack.LastSkipped.HasValue && _currentTrack.LastSkipped.Value != DateTime.MinValue) { var daysSinceLastSkip = (DateTime.UtcNow - _currentTrack.LastSkipped.Value).TotalDays; _currentTrack.SkipCount = (int)Math.Round(_currentTrack.SkipCount * Math.Pow(0.5, daysSinceLastSkip)); }
            _currentTrack.SkipCount++;
            _currentTrack.LastSkipped = DateTime.UtcNow;
            _library.Update(_currentTrack);
            Log.Information("[Player] Skip penalty recorded for '{Title}' (skipped after {Elapsed:F1}s, decayed total skips: {Count})", _currentTrack.Title, elapsed, _currentTrack.SkipCount);
            NullActionLogger.User("SkipPenalty", $"track={_currentTrack.Id} elapsed={elapsed:F1}s skips={_currentTrack.SkipCount}", nameof(PlayerViewModel));
        }
    }

    // Playlist-aware auto-fill: the queue panel now shows upcoming playlist tracks
    // (sequential order, or shuffle-deck order when shuffle is on).
    private void RefillAutoQueue()
    {
        var target = _settings.QueueAutoFillSize;
        var autoCount = _library.GetQueue().Count(e => !e.IsManual);
        var needed = target - autoCount;
        if (needed <= 0) return;

        if (_activePlaylist != null && _activePlaylist.Tracks.Count > 0)
        {
            var adds = new System.Collections.Generic.List<Track>();
            if (IsShuffle)
            {
                for (int i = 0; i < needed; i++)
                {
                    var t = DrawPlaylistShuffle();
                    if (t == null) break;
                    adds.Add(t);
                }
            }
            else
            {
                var start = (_currentTrack != null ? _activePlaylist.Tracks.IndexOf(_currentTrack) : _activePlaylistIndex) + 1;
                for (int i = 0; i < needed && start + i < _activePlaylist.Tracks.Count; i++)
                    adds.Add(_activePlaylist.Tracks[start + i]);
            }
            if (adds.Count > 0) _library.FillQueue(adds);
            return;
        }

        var upcoming = _navigator.GenerateUpcoming(needed);
        _library.FillQueue(upcoming);
    }

    private void CheckCrossfade(float pos)
    {
        if (_currentTrack?.MediaType is MediaType.Radio or MediaType.Audiobook) return;
        if (!_settings.CrossfadeEnabled || _isCrossfading || _hasTriggeredCrossfade || _currentTrack == null) return;
        var duration = _playback.Duration.TotalSeconds;
        if (duration <= 0) return;
        var remaining = duration - (pos * duration);
        if (remaining <= _settings.CrossfadeDurationSeconds)
        {
            _hasTriggeredCrossfade = true;
            Track? next = null;

            var head = _library.GetQueue();
            if (head.Count > 0)
            {
                _queueInterrupt = head[0].IsManual;
                next = head[0].Track;
                _library.DequeueNext();
            }
            else if (_activePlaylist != null && _activePlaylist.Tracks.Count > 0)
            {
                if (IsShuffle) next = DrawPlaylistShuffle();
                else
                {
                    var index = _activePlaylist.Tracks.IndexOf(_currentTrack);
                    next = index >= 0 && index < _activePlaylist.Tracks.Count - 1
                        ? _activePlaylist.Tracks[index + 1]
                        : (_navigator.RepeatMode == RepeatMode.All ? _activePlaylist.Tracks[0] : null);
                }
            }
            else
            {
                next = _navigator.GetNextTrack(_currentTrack);
            }

            if (next != null && _activePlaylist != null)
            {
                int idx = _activePlaylist.Tracks.IndexOf(next);
                if (idx >= 0) _activePlaylistIndex = idx; else if (!_queueInterrupt) _activePlaylist = null;
                _queueInterrupt = false;
                RebuildAutoQueue();
            }

            if (next != null && !string.IsNullOrEmpty(next.FilePath) && File.Exists(next.FilePath))
            {
                _isCrossfading = true;
                var left = _currentTrack;

                CurrentTrack = next;
                _navigator.CurrentTrack = next;
                AlbumArtPath = next.AlbumArtPath;
                StatusText = CurrentTrackDisplay;
                _trackStartTime = DateTime.UtcNow;

                Log.Information("Starting crossfade transition to {NextTitle}", next.Title);
                var sessionId = _playSessionId;
                var crossfadeTask = _playback.CrossfadeToAsync(next.FilePath, _settings.CrossfadeDurationSeconds * 1000, _volume);

                _ = crossfadeTask.ContinueWith(t =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (sessionId != _playSessionId) return;
                        _isCrossfading = false;
                        if (!t.IsCompletedSuccessfully || t.Result != _playback.CrossfadeGeneration) return;

                        if (left != null) _navigator.RecordPlay(left);
                        if (left != null && _playRecorded && left.MediaType != MediaType.Audiobook)
                            TrackScrobbleRequested?.Invoke(left.Title, left.Artist, DateTime.UtcNow);

                        _playRecorded = false;
                        _hasTriggeredCrossfade = false;
                        NullActionLogger.TrackPlayed(next.Id.ToString(), next.Title, next.Artist, nameof(PlayerViewModel));
                    });
                });
            }
            else
            {
                Log.Debug("[PlayerViewModel] Approaching end of playlist or no valid next track. Crossfade bypassed.");
            }
        }
    }

    private void CheckPlayRecorded()
    {
        if (_currentTrack == null || _playRecorded) return;
        var duration = _playback.Duration.TotalSeconds;
        if (duration <= 0) return;
        var elapsed = Position * duration;
        if (elapsed >= Math.Min(30, duration * _settings.ScrobbleThreshold)) { _playRecorded = true; _library.RecordPlay(_currentTrack.Id); }
    }

    private void StartSleepTimer(int minutes)
    {
        CancelSleepTimer();
        SleepTimerRemaining = TimeSpan.FromMinutes(minutes);
        _sleepTimer = new System.Threading.Timer(_ =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (SleepTimerRemaining.TotalSeconds <= 1)
                {
                    CancelSleepTimer();
                    if (IsPlaying) { _ = _playback.FadeAndPauseAsync(1500).ContinueWith(_ => Dispatcher.UIThread.Post(() => { Stop(); ToastService.Instance.Show(L("MiniPlayer_SleepTimer_Stopped"), ToastType.Info, scope: "sleep"); })); }
                    return;
                }
                SleepTimerRemaining = SleepTimerRemaining.Add(TimeSpan.FromSeconds(-1));
            });
        }, null, 1000, 1000);
        ToastService.Instance.Show(string.Format(L("MiniPlayer_SleepTimer_Started"), minutes), ToastType.Success, scope: "sleep");
    }

    private void CancelSleepTimer()
    {
        _sleepTimer?.Dispose();
        _sleepTimer = null;
        SleepTimerRemaining = TimeSpan.Zero;
    }
}