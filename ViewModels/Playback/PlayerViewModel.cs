using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Media;
using Material.Icons;
using NullWave.Helpers;
using NullWave.Helpers.Logging;
using NullWave.Models;
using NullWave.Services;
using NullWave.ViewModels.Base;
using Serilog;

namespace NullWave.ViewModels;

public enum ShuffleMode { Off, Normal, Smart }

public class PlayerViewModel : ViewModelBase
{
    private readonly PlaybackService _playback;
    private readonly DownloadService _download;
    private readonly LibraryService _library;
    private readonly SettingsViewModel _settings;
    private readonly PlaybackNavigator _navigator;

    private Track? _currentTrack;
    private PlaybackState _state = PlaybackState.Stopped;
    private float _position;
    private float _volume = 0.8f;
    private bool _isDownloading;
    private float _downloadProgress;
    private string _statusText = "No track playing";
    private DateTime _trackStartTime = DateTime.MinValue;
    private bool _isCrossfading;
    private bool _hasTriggeredCrossfade;

    private float _volumeBeforeMute = 0.8f;
    private bool _isMuted;
    private ShuffleMode _shuffleMode = ShuffleMode.Off;

    public event Action<string, string, DateTime>? TrackScrobbleRequested;
    public event Action? PlaySelectedTrackRequested;

    private DateTime _lastNavigationTime = DateTime.MinValue;
    private static readonly TimeSpan NavigationDebounce = TimeSpan.FromMilliseconds(300);
    private readonly MetadataService _metadata;

    public PlayerViewModel(
        PlaybackService playback,
        DownloadService download,
        LibraryService library,
        SettingsViewModel settings,
        MetadataService metadata)
    {
        _playback = playback;
        _download = download;
        _library = library;
        _settings = settings;
        _metadata = metadata;
        _navigator = new PlaybackNavigator(library);

        _playback.Volume = _volume;
        _playback.PositionChanged += pos =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() => 
            {
                Position = pos;
                CheckCrossfade(pos);
            });
        _playback.StateChanged += state =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() => State = state);
        _playback.TrackFinished += () =>
            Avalonia.Threading.Dispatcher.UIThread.Post(OnTrackFinished);

        _download.ProgressChanged += (_, pct) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                DownloadProgress = pct;
                StatusText = $"Downloading... {pct:P0}";
            });
        };

        _download.DownloadCompleted += (trackId, filePath, isInteractive) =>
        {
            if (!isInteractive) return;

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                IsDownloading = false;
                StatusText = "Download complete";
                NullActionLogger.ImportCompleted(filePath, trackId, 0, nameof(PlayerViewModel));
                if (Guid.TryParse(trackId, out var id))
                {
                    var track = _library.GetAll().FirstOrDefault(t => t.Id == id);
                    if (track != null)
                    {
                        track.FilePath = filePath;

                        if (track.Title == track.Url
                            || track.Title == "Unknown Title"
                            || string.IsNullOrWhiteSpace(track.Title)
                            || track.Artist == "Unknown"
                            || string.IsNullOrWhiteSpace(track.Artist))
                        {
                            var (tagTitle, tagArtist, duration) = _metadata.FetchFromLocalFile(filePath);
                            if (!string.IsNullOrWhiteSpace(tagTitle)
                                && tagTitle != System.IO.Path.GetFileNameWithoutExtension(filePath))
                            {
                                if (track.Title == track.Url
                                    || track.Title == "Unknown Title"
                                    || string.IsNullOrWhiteSpace(track.Title))
                                    track.Title = tagTitle;
                                if (track.Artist == "Unknown" || string.IsNullOrWhiteSpace(track.Artist))
                                    track.Artist = tagArtist;
                            }
                            track.Duration = duration;
                        }

                        _library.Update(track);

                        var fresh = _library.GetAll().FirstOrDefault(t => t.Id == id);
                        var toPlay = fresh ?? track;
                        PlayTrack(toPlay);
                    }
                }
            });
        };

        _download.DownloadFailed += (trackId, error, isInteractive) =>
        {
            if (!isInteractive) return;

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                IsDownloading = false;
                StatusText = $"Download failed: {error}";
                NullActionLogger.Error(nameof(PlayerViewModel), $"Download failed: {error}", $"trackId={trackId}");
            });
        };

        PlayPauseCommand = new RelayCommand(PlayPause);
        StopCommand = new RelayCommand(Stop);
        PlayTrackCommand = new RelayCommand<Track>(PlayTrack);
        DownloadTrackCommand = new RelayCommand<Track>(async t =>
        {
            if (t?.Url == null) return;
            IsDownloading = true;
            StatusText = "Starting download...";
            NullActionLogger.ImportStarted(t.Url, nameof(PlayerViewModel));
            await _download.DownloadAsync(t.Id.ToString(), t.Url, _settings.AudioFormat, _settings.AudioQuality);
        });

        SeekBackwardCommand = new RelayCommand(() => SeekRelative(-10));
        SeekForwardCommand = new RelayCommand(() => SeekRelative(10));
        PreviousTrackCommand = new RelayCommand(PlayPrevious);
        NextTrackCommand = new RelayCommand(PlayNext);

        CycleShuffleCommand = new RelayCommand(() =>
        {
            var aiEnabled = _settings.AIFeaturesEnabled;
            ShuffleMode = (ShuffleMode, aiEnabled) switch
            {
                (ShuffleMode.Off,    _)     => ShuffleMode.Normal,
                (ShuffleMode.Normal, true)  => ShuffleMode.Smart,
                (ShuffleMode.Normal, false) => ShuffleMode.Off,
                (ShuffleMode.Smart,  _)     => ShuffleMode.Off,
                _                           => ShuffleMode.Off
            };

            _navigator.IsShuffle      = IsShuffle;
            _navigator.IsSmartShuffle = _shuffleMode == ShuffleMode.Smart && aiEnabled;
        });

        CycleRepeatCommand = new RelayCommand(() =>
        {
            _navigator.CycleRepeat();
            OnPropertyChanged(nameof(RepeatMode));
            OnPropertyChanged(nameof(RepeatIconKind));
            OnPropertyChanged(nameof(RepeatTooltip));
            OnPropertyChanged(nameof(IsRepeat));
            OnPropertyChanged(nameof(RepeatForeground));
        });

        ToggleMuteCommand = new RelayCommand(() =>
        {
            if (_isMuted)
            {
                _isMuted = false;
                Volume = _volumeBeforeMute;
            }
            else
            {
                _volumeBeforeMute = _volume > 0 ? _volume : 0.8f;
                _isMuted = true;
                Volume = 0;
            }
        });

        ToggleCurrentFavoriteCommand = new RelayCommand(() =>
        {
            if (_currentTrack == null) return;
            _library.ToggleFavorite(_currentTrack.Id);
            OnPropertyChanged(nameof(IsCurrentFavorite));
        });
    }

    public void UpdateSkipPenaltyCap(int cap)
    {
        _navigator.SkipPenaltyCap = cap;
    }

    private void RecordSkipIfEarly()
    {
        if (_currentTrack == null) return;
        if (_trackStartTime == DateTime.MinValue) return;
        
        var elapsed = (DateTime.UtcNow - _trackStartTime).TotalSeconds;
        var window  = _settings.SkipPenaltyWindowSeconds;

        if (elapsed < 0.5) return;

        if (elapsed <= window)
        {
            _currentTrack.SkipCount++;
            _currentTrack.LastSkipped = DateTime.UtcNow;
            _library.Update(_currentTrack);

            Log.Information("[Player] Skip penalty recorded for '{Title}' " +
                "(skipped after {Elapsed:F1}s, total skips: {Count})",
                _currentTrack.Title, elapsed, _currentTrack.SkipCount);

            NullActionLogger.User("SkipPenalty",
                $"track={_currentTrack.Id} elapsed={elapsed:F1}s skips={_currentTrack.SkipCount}",
                nameof(PlayerViewModel));
        }
    }

    private void CheckCrossfade(float pos)
    {
        if (!_settings.CrossfadeEnabled || _isCrossfading || _hasTriggeredCrossfade || _currentTrack == null) return;
        
        var duration = _playback.Duration.TotalSeconds;
        if (duration <= 0) return;

        var remaining = duration - (pos * duration);
        if (remaining <= _settings.CrossfadeDurationSeconds)
        {
            _hasTriggeredCrossfade = true;

            // Queue takes priority for crossfade target too
            var queue = _library.GetQueue();
            var next = queue.Count > 0 ? queue[0] : _navigator.GetNextTrack(_currentTrack);
            
            if (next != null && !string.IsNullOrEmpty(next.FilePath) && System.IO.File.Exists(next.FilePath))
            {
                _isCrossfading = true;
                Log.Information("Starting crossfade transition to {NextTitle}", next.Title);
                
                _ = _playback.CrossfadeToAsync(next.FilePath, _settings.CrossfadeDurationSeconds * 1000, _volume).ContinueWith(t => 
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => 
                    {
                        CurrentTrack = next;
                        AlbumArtPath = next.AlbumArtPath;
                        _trackStartTime = DateTime.UtcNow;
                        StatusText = CurrentTrackDisplay;
                        NullActionLogger.TrackPlayed(next.Id.ToString(), next.Title, next.Artist, nameof(PlayerViewModel));
                        _isCrossfading = false;
                    });
                });
            }
            else
            {
                Log.Debug("[PlayerViewModel] Approaching end of playlist or no valid next track. Crossfade bypassed.");
            }
        }
    }

    public Track? CurrentTrack
    {
        get => _currentTrack;
        private set
        {
            _currentTrack = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentTrackDisplay));
            OnPropertyChanged(nameof(HasAlbumArt));
            OnPropertyChanged(nameof(AlbumArtPath));
            OnPropertyChanged(nameof(IsCurrentFavorite));
        }
    }

    public string CurrentTrackDisplay => _currentTrack == null
        ? "No track playing"
        : $"{_currentTrack.Artist} - {_currentTrack.Title}";

    private string? _albumArtPath;
    public string? AlbumArtPath
    {
        get => _albumArtPath;
        set
        {
            _albumArtPath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasAlbumArt));
        }
    }

    public bool HasAlbumArt => !string.IsNullOrEmpty(_albumArtPath);

    public PlaybackState State
    {
        get => _state;
        private set
        {
            _state = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsPlaying));
            OnPropertyChanged(nameof(PlayPauseIconKind));
        }
    }

    public bool IsPlaying => _state == PlaybackState.Playing;

    public MaterialIconKind PlayPauseIconKind => IsPlaying ? MaterialIconKind.Pause : MaterialIconKind.Play;

    public float Position
    {
        get => _position;
        set
        {
            _position = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PositionDisplay));
        }
    }

    public string PositionDisplay
    {
        get
        {
            var total = _playback.Duration;
            var current = TimeSpan.FromSeconds(Position * total.TotalSeconds);
            return $"{(int)current.TotalMinutes:D2}:{current.Seconds:D2}";
        }
    }

    public string DurationDisplay
    {
        get
        {
            var total = _playback.Duration;
            return $"{(int)total.TotalMinutes:D2}:{total.Seconds:D2}";
        }
    }

    public float Volume
    {
        get => _volume;
        set
        {
            _volume = value;
            _playback.Volume = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(VolumeIconKind));
        }
    }

    public bool IsDownloading
    {
        get => _isDownloading;
        private set { _isDownloading = value; OnPropertyChanged(); }
    }

    private bool _smartShuffleEnabled;
    public bool SmartShuffleEnabled
    {
        get => _smartShuffleEnabled;
        set
        {
            if (_smartShuffleEnabled == value) return;
            _smartShuffleEnabled = value;
            OnPropertyChanged();

            _navigator.IsSmartShuffle = value;
            if (value && _shuffleMode == ShuffleMode.Off)
            {
                _shuffleMode = ShuffleMode.Smart;
                _navigator.IsShuffle = true;
                OnPropertyChanged(nameof(ShuffleMode));
                OnPropertyChanged(nameof(IsShuffle));
                OnPropertyChanged(nameof(ShuffleIconKind));
                OnPropertyChanged(nameof(ShuffleTooltip));
                OnPropertyChanged(nameof(ShuffleForeground));
            }
            else if (!value && _shuffleMode == ShuffleMode.Smart)
            {
                _shuffleMode = ShuffleMode.Normal;
                OnPropertyChanged(nameof(ShuffleMode));
                OnPropertyChanged(nameof(ShuffleIconKind));
                OnPropertyChanged(nameof(ShuffleTooltip));
                OnPropertyChanged(nameof(ShuffleForeground));
            }
        }
    }

    public float DownloadProgress
    {
        get => _downloadProgress;
        private set { _downloadProgress = value; OnPropertyChanged(); }
    }

    public string StatusText
    {
        get => _statusText;
        private set { _statusText = value; OnPropertyChanged(); }
    }

    private bool _showAlreadyPlayingToast;
    public bool ShowAlreadyPlayingToast
    {
        get => _showAlreadyPlayingToast;
        private set { _showAlreadyPlayingToast = value; OnPropertyChanged(); }
    }

    public ShuffleMode ShuffleMode
    {
        get => _shuffleMode;
        private set
        {
            _shuffleMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsShuffle));
            OnPropertyChanged(nameof(ShuffleIconKind));
            OnPropertyChanged(nameof(ShuffleTooltip));
            OnPropertyChanged(nameof(ShuffleForeground));
        }
    }

    public bool IsShuffle => _shuffleMode != ShuffleMode.Off;
    public RepeatMode RepeatMode => _navigator.RepeatMode;
    public bool IsRepeat => _navigator.RepeatMode != RepeatMode.None;

    public MaterialIconKind ShuffleIconKind => _shuffleMode == ShuffleMode.Smart ? MaterialIconKind.AutoFixHigh : MaterialIconKind.Shuffle;

    public MaterialIconKind RepeatIconKind => RepeatMode switch
    {
        RepeatMode.One => MaterialIconKind.RepeatOnce,
        RepeatMode.All => MaterialIconKind.Repeat,
        _ => MaterialIconKind.RepeatOff
    };

    public MaterialIconKind VolumeIconKind => _isMuted || _volume == 0 ? MaterialIconKind.VolumeMute : (_volume < 0.5f ? MaterialIconKind.VolumeLow : MaterialIconKind.VolumeHigh);

    public string ShuffleTooltip => _shuffleMode switch
    {
        ShuffleMode.Normal => "Shuffle: Normal",
        ShuffleMode.Smart => "Smart Shuffle (AI)",
        _ => "Shuffle: Off"
    };

    public string RepeatTooltip => RepeatMode switch
    {
        RepeatMode.One => "Repeat: One",
        RepeatMode.All => "Repeat: All",
        _ => "Repeat: Off"
    };

    public IBrush ShuffleForeground => _shuffleMode switch
    {
        ShuffleMode.Normal => new SolidColorBrush(Color.Parse("#8B5CF6")),
        ShuffleMode.Smart => new SolidColorBrush(Color.Parse("#FCD34D")),
        _ => new SolidColorBrush(Color.Parse("#A8B4CC"))
    };

    public IBrush RepeatForeground => IsRepeat
        ? new SolidColorBrush(Color.Parse("#8B5CF6"))
        : new SolidColorBrush(Color.Parse("#A8B4CC"));

    public bool IsCurrentFavorite => _currentTrack?.IsFavorite ?? false;

    public ICommand PlayPauseCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand PlayTrackCommand { get; }
    public ICommand DownloadTrackCommand { get; }
    public ICommand SeekBackwardCommand { get; }
    public ICommand SeekForwardCommand { get; }
    public ICommand PreviousTrackCommand { get; }
    public ICommand NextTrackCommand { get; }
    public ICommand CycleShuffleCommand { get; }
    public ICommand CycleRepeatCommand { get; }
    public ICommand ToggleMuteCommand { get; }
    public ICommand ToggleCurrentFavoriteCommand { get; }

    public void PlayTrack(Track? track)
    {
        if (track == null) return;

        _hasTriggeredCrossfade = false;

        if (_currentTrack != null && track.Id == _currentTrack.Id
            && _state == PlaybackState.Playing)
        {
            ShowAlreadyPlayingToast = true;
            _ = Task.Run(async () => {
                await Task.Delay(1500);
                Avalonia.Threading.Dispatcher.UIThread.Post(() => ShowAlreadyPlayingToast = false);
            });
            return;
        }

        CurrentTrack = track;
        AlbumArtPath = track.AlbumArtPath;

        if (!string.IsNullOrEmpty(track.FilePath) && System.IO.File.Exists(track.FilePath))
        {
            _playback.Play(track.FilePath);
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
                StatusText = "Downloading before playback...";
                NullActionLogger.ImportStarted(track.Url, nameof(PlayerViewModel));
                _ = _download.DownloadAsync(
                    track.Id.ToString(), track.Url,
                    _settings.AudioFormat, _settings.AudioQuality);
            }
            else
            {
                StatusText = "Download already in progress...";
                Log.Debug("[{Source}] Skipped duplicate download for {Url}",
                    nameof(PlayerViewModel), track.Url);
            }
            return;
        }

        StatusText = "No playable source found";
    }

    private void PlayPause()
    {
        if (IsPlaying)
        {
            if (_settings.FadeOnPauseEnabled)
                _ = _playback.FadeAndPauseAsync(_settings.FadeOnPauseDurationMs);
            else
                _playback.Pause();
                
            NullActionLogger.TrackPaused(_currentTrack?.Id.ToString() ?? "?", PositionDisplay, nameof(PlayerViewModel));
        }
        else if (_state == PlaybackState.Paused)
        {
            if (_settings.FadeOnPauseEnabled)
                _ = _playback.FadeAndResumeAsync(_settings.FadeOnPauseDurationMs);
            else
                _playback.Resume();
                
            if (_currentTrack != null)
                NullActionLogger.TrackPlayed(_currentTrack.Id.ToString(), _currentTrack.Title, _currentTrack.Artist, nameof(PlayerViewModel));
        }
        else if (_currentTrack != null)
        {
            PlayTrack(_currentTrack);
        }
        else
        {
            PlaySelectedTrackRequested?.Invoke();
        }
    }

    private void Stop()
    {
        _playback.Stop();
        if (_currentTrack != null)
            NullActionLogger.TrackStopped(_currentTrack.Id.ToString(), nameof(PlayerViewModel));
    }

    private void OnTrackFinished()
    {
        if (_currentTrack != null)
        {
            _library.RecordPlay(_currentTrack.Id);
            NullActionLogger.TrackStopped(_currentTrack.Id.ToString(), nameof(PlayerViewModel));

            if (_position >= _settings.ScrobbleThreshold)
                TrackScrobbleRequested?.Invoke(_currentTrack.Title, _currentTrack.Artist, DateTime.UtcNow);
        }

        if (_navigator.ShouldRepeatCurrent() && _currentTrack != null)
            PlayTrack(_currentTrack);
        else
            PlayNext();
    }

    private void SeekRelative(int seconds)
    {
        var duration = _playback.Duration.TotalSeconds;
        if (duration <= 0) return;
        var newPosition = Math.Clamp(Position * duration + seconds, 0, duration);
        Position = (float)(newPosition / duration);
        _playback.Seek(Position);
    }

    public void SeekTo(float position)
    {
        Position = Math.Clamp(position, 0f, 1f);
        _playback.Seek(Position);
    }

    private void PlayPrevious()
    {
        if (DateTime.UtcNow - _lastNavigationTime < NavigationDebounce) return;
        _lastNavigationTime = DateTime.UtcNow;
        RecordSkipIfEarly();

        _download.CancelCurrentDownload();
        IsDownloading = false;
        var prev = _navigator.GetPreviousTrack(_currentTrack);
        if (prev != null) PlayTrack(prev);
    }

    private void PlayNext()
    {
        if (DateTime.UtcNow - _lastNavigationTime < NavigationDebounce) return;
        _lastNavigationTime = DateTime.UtcNow;
        RecordSkipIfEarly();

        _download.CancelCurrentDownload();
        IsDownloading = false;

        // Queue takes priority over normal shuffle/repeat/library navigation
        var queued = _library.DequeueNext();
        if (queued != null)
        {
            PlayTrack(queued);
            return;
        }

        var next = _navigator.GetNextTrack(_currentTrack);
        if (next != null) PlayTrack(next);
        else StatusText = "End of library";
    }
}