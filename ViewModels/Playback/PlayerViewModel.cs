using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
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
    private readonly MetadataService _metadata;

    private Track? _currentTrack;
    private PlaybackState _state = PlaybackState.Stopped;
    private float _position;
    private float _volume = 0.8f;
    private bool _isDownloading;
    private float _downloadProgress;
    private string _statusText = LocalizationService.Instance["Player_Status_NoTrack"];
    private DateTime _trackStartTime = DateTime.MinValue;
    private bool _isCrossfading;
    private bool _hasTriggeredCrossfade;
    private bool _playRecorded;
    private Guid _playSessionId = Guid.NewGuid();
    private long _pendingResumeTicks;
    private System.Threading.Timer? _audiobookSaveTimer;
    
    // FIX: Flag to prevent Previous track oscillation
    private bool _suppressHistoryRecord;

    // NEW: SLEEP TIMER STATE
    private System.Threading.Timer? _sleepTimer;
    private TimeSpan _sleepTimerRemaining;
    private bool _sleepTimerEndOfChapter;
    public bool SleepTimerEndOfChapter
    {
        get => _sleepTimerEndOfChapter;
        set { _sleepTimerEndOfChapter = value; OnPropertyChanged(); }
    }
    public ICommand SetEndOfChapterTimerCommand { get; }

    private Playlist? _activePlaylist;
    private int _activePlaylistIndex = -1;

    private float _volumeBeforeMute = 0.8f;
    private bool _isMuted;
    private ShuffleMode _shuffleMode = ShuffleMode.Off;

    private string _nowPlayingTitle = string.Empty;
    private string _nowPlayingArtist = string.Empty;
    private float _playbackRate = 1.0f;
    public float PlaybackRate
    {
        get => _playbackRate;
        set
        {
            _playbackRate = value;
            _playback.SetRate(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(PlaybackRateDisplay));
            OnPropertyChanged(nameof(PlaybackRateForeground));
            OnPropertyChanged(nameof(IsRateAdjusted));
            _settings.AudiobookPlaybackRate = value;
        }
    }

    public string PlaybackRateDisplay => $"{_playbackRate:0.0#}x";
    public ICommand SetPlaybackRateCommand { get; }

    public event Action<string, string, DateTime>? TrackScrobbleRequested;
    public event Action? PlaySelectedTrackRequested;

    private DateTime _lastNavigationTime = DateTime.MinValue;
    private static readonly TimeSpan NavigationDebounce = TimeSpan.FromMilliseconds(300);

    private static string L(string key) => LocalizationService.Instance[key];

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
        _playbackRate = _settings.AudiobookPlaybackRate;

        _playback.Volume = _volume;
        _playback.PositionChanged += pos =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                Position = pos;
                CheckPlayRecorded();
                CheckCrossfade(pos);
            });
        _playback.StateChanged += state =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                State = state;
                if (state == PlaybackState.Playing && _pendingResumeTicks > 0 && _playback.Duration > TimeSpan.Zero)
                {
                    var frac = (float)Math.Clamp(_pendingResumeTicks / (double)_playback.Duration.Ticks, 0, 0.98);
                    _playback.Seek(frac);
                    _pendingResumeTicks = 0;
                }

                if (state is PlaybackState.Paused or PlaybackState.Stopped)
                    SaveAudiobookProgress();
            });
        _playback.TrackFinished += () =>
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_currentTrack?.MediaType == MediaType.Radio) return;

            if (_sleepTimerEndOfChapter && _currentTrack?.MediaType == MediaType.Audiobook)
            {
                SleepTimerEndOfChapter = false;
                _ = _playback.FadeAndPauseAsync(1500).ContinueWith(_ =>
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        Stop();
                        ToastService.Instance.Show(L("MiniPlayer_SleepTimer_ChapterStopped"), ToastType.Info, scope: "sleep");
                    });
                });
                return;
            }

            OnTrackFinished();
        });

        _playback.DurationDiscovered += duration =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                var track = CurrentTrack;
                if (track != null && track.Duration == TimeSpan.Zero && duration > TimeSpan.Zero)
                {
                    track.Duration = duration;
                    _library.Update(track);
                }
            });
        };

        _playback.RadioMetadataChanged += (title, artist) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (_currentTrack?.MediaType == MediaType.Radio)
                    StatusText = string.IsNullOrWhiteSpace(artist) ? title : $"{artist} - {title}";
            });
        };

        _playback.StreamFailed += reason =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (_currentTrack?.MediaType != MediaType.Radio) return;
                StatusText = $"{_currentTrack.Title} - stream unavailable";
                ToastService.Instance.Show(
                    $"'{_currentTrack.Title}' stream is unreachable. Try another station.",
                    ToastType.Warning, scope: "radio");
            });
        };

        _download.ProgressChanged += (_, pct) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                DownloadProgress = pct;
                StatusText = string.Format(L("Player_Status_DownloadingPct"), pct);
            });
        };

        _download.DownloadCompleted += (trackId, filePath, isInteractive) =>
        {
            if (!isInteractive || string.IsNullOrEmpty(filePath)) return;

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                IsDownloading = false;
                StatusText = L("Player_Status_DownloadComplete");
                NullActionLogger.ImportCompleted(filePath, trackId, 0, nameof(PlayerViewModel));
                if (Guid.TryParse(trackId, out var id))
                {
                    var track = _library.GetAll().FirstOrDefault(t => t.Id == id);
                    if (track != null)
                    {
                        track.FilePath = filePath;

                        var (tagTitle, tagArtist, duration) = _metadata.FetchFromLocalFile(filePath);
                        if (!string.IsNullOrWhiteSpace(tagTitle))
                        {
                            if (track.Title == track.Url
                                || track.Title == "Unknown Title"
                                || string.IsNullOrWhiteSpace(track.Title))
                                track.Title = tagTitle;

                            if (track.Artist == "Unknown"
                                || track.Artist == "Unknown Artist"
                                || string.IsNullOrWhiteSpace(track.Artist))
                                track.Artist = tagArtist;
                        }
                        track.Duration = duration;

                        _library.Update(track);
                        _library.NormalizeLocalFile(track);

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
                StatusText = string.Format(L("Player_Status_DownloadFailed"), error ?? L("Player_Status_UnknownError"));
                NullActionLogger.Error(nameof(PlayerViewModel), $"Download failed: {error ?? "Unknown error"}", $"trackId={trackId}");

                if (Guid.TryParse(trackId, out var id))
                {
                    var failedTrack = _library.GetAll().FirstOrDefault(t => t.Id == id);
                    if (failedTrack != null)
                    {
                        var retryTarget = failedTrack;
                        ToastService.Instance.Show(
                            message: $"Failed to download '{retryTarget?.Title ?? "Unknown Track"}'",
                            type: ToastType.Error,
                            durationMs: 6000,
                            detailedMessage: error ?? "An unknown error occurred during download.",
                            actionText: "Retry",
                            actionCallback: () => { if (retryTarget != null) DownloadTrackCommand?.Execute(retryTarget); },
                            scope: "download-fail");
                    }
                }
            });
        };

        PlayPauseCommand = new RelayCommand(PlayPause);
        StopCommand = new RelayCommand(Stop);
        PlayTrackCommand = new RelayCommand<Track>(PlayTrack);
        DownloadTrackCommand = new RelayCommand<Track>(async t =>
        {
            if (t?.Url == null) return;
            IsDownloading = true;
            StatusText = L("Player_Status_StartingDownload");
            NullActionLogger.ImportStarted(t.Url, nameof(PlayerViewModel));
            await _download.DownloadAsync(t.Id.ToString(), t.Url, _settings.AudioFormat, _settings.AudioQuality);
        });

        SeekBackwardCommand = new RelayCommand(() => SeekRelative(-10));
        SeekForwardCommand = new RelayCommand(() => SeekRelative(10));
        SkipBack30Command = new RelayCommand(() => SeekRelative(-30));
        SkipForward30Command = new RelayCommand(() => SeekRelative(30));
        PreviousTrackCommand = new RelayCommand(PlayPrevious);
        NextTrackCommand = new RelayCommand(PlayNext);

        // NEW: SLEEP TIMER COMMANDS
        SetSleepTimerCommand = new RelayCommand<string>(s => { if (int.TryParse(s, out var m)) StartSleepTimer(m); });
        CancelSleepTimerCommand = new RelayCommand(CancelSleepTimer);
        SetEndOfChapterTimerCommand = new RelayCommand(() => { CancelSleepTimer(); SleepTimerEndOfChapter = true; ToastService.Instance.Show(L("MiniPlayer_SleepTimer_ChapterSet"), ToastType.Success, scope: "sleep"); });
        SetPlaybackRateCommand = new RelayCommand<string>(s => { if (float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var r)) PlaybackRate = r; });

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

            _library.ClearAutoQueue();
            RefillAutoQueue();
        });

        CycleRepeatCommand = new RelayCommand(() =>
        {
            _navigator.CycleRepeat();
            OnPropertyChanged(nameof(RepeatMode));
            OnPropertyChanged(nameof(RepeatIconKind));
            OnPropertyChanged(nameof(RepeatTooltip));
            OnPropertyChanged(nameof(IsRepeat));
            OnPropertyChanged(nameof(RepeatForeground));

            _library.ClearAutoQueue();
            RefillAutoQueue();
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

    // NEW: SLEEP TIMER METHODS
    private void StartSleepTimer(int minutes)
    {
        CancelSleepTimer();
        SleepTimerRemaining = TimeSpan.FromMinutes(minutes);
        _sleepTimer = new System.Threading.Timer(_ =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (SleepTimerRemaining.TotalSeconds <= 1)
                {
                    CancelSleepTimer();
                    if (IsPlaying)
                    {
                        _ = _playback.FadeAndPauseAsync(1500).ContinueWith(_ =>
                        {
                            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                            {
                                Stop();
                                ToastService.Instance.Show(L("MiniPlayer_SleepTimer_Stopped"), ToastType.Info, scope: "sleep");
                            });
                        });
                    }
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

    public TimeSpan SleepTimerRemaining
    {
        get => _sleepTimerRemaining;
        private set
        {
            _sleepTimerRemaining = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SleepTimerDisplay));
            OnPropertyChanged(nameof(IsSleepTimerActive));
        }
    }

    public bool IsSleepTimerActive => _sleepTimerRemaining > TimeSpan.Zero;

    public string SleepTimerDisplay => IsSleepTimerActive
        ? $"{(int)_sleepTimerRemaining.TotalMinutes:D2}:{_sleepTimerRemaining.Seconds:D2}"
        : "";

    public void UpdateSkipPenaltyCap(int cap)
    {
        _navigator.SkipPenaltyCap = cap;
    }

    private void RefillAutoQueue()
    {
        if (_activePlaylist != null) return;

        var currentAutoCount = _library.GetQueue().Count(e => !e.IsManual);
        var target = _settings.QueueAutoFillSize;
        if (currentAutoCount >= target) return;

        var needed = target - currentAutoCount;
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

            Track? next;
            if (_activePlaylist != null)
            {
                if (IsShuffle)
                {
                    var candidates = _activePlaylist.Tracks.Where(t => t.Id != _currentTrack.Id).ToList();
                    next = candidates.Count > 0 ? candidates[Random.Shared.Next(candidates.Count)] : null;
                }
                else
                {
                    var index = _activePlaylist.Tracks.IndexOf(_currentTrack);
                    next = index >= 0 && index < _activePlaylist.Tracks.Count - 1
                        ? _activePlaylist.Tracks[index + 1]
                        : null;
                }
            }
            else
            {
                var queueEntries = _library.GetQueue();
                if (queueEntries.Count > 0)
                {
                    next = queueEntries[0].Track;
                    _library.DequeueNext();
                }
                else
                {
                    next = _navigator.GetNextTrack(_currentTrack);
                }
            }

            if (next != null && !string.IsNullOrEmpty(next.FilePath) && System.IO.File.Exists(next.FilePath))
            {
                _isCrossfading = true;
                Log.Information("Starting crossfade transition to {NextTitle}", next.Title);

                var sessionId = _playSessionId;
                var left = _currentTrack; // Capture outgoing track
                var crossfadeTask = _playback.CrossfadeToAsync(
                    next.FilePath, _settings.CrossfadeDurationSeconds * 1000, _volume);

                _ = crossfadeTask.ContinueWith(t =>
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        if (sessionId != _playSessionId)
                            return;

                        _isCrossfading = false;

                        if (!t.IsCompletedSuccessfully
                            || t.Result != _playback.CrossfadeGeneration)
                            return;

                        // FIX: Record play for the outgoing track
                        if (left != null) _navigator.RecordPlay(left);
                        
                        // FIX: Scrobble outgoing track if it met the threshold
                        if (left != null && _playRecorded && left.MediaType != MediaType.Audiobook)
                            TrackScrobbleRequested?.Invoke(left.Title, left.Artist, DateTime.UtcNow);

                        CurrentTrack = next;
                        _navigator.CurrentTrack = next;
                        _playRecorded = false;

                        AlbumArtPath = next.AlbumArtPath;
                        _trackStartTime = DateTime.UtcNow;
                        _hasTriggeredCrossfade = false;
                        StatusText = CurrentTrackDisplay;
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
        if (elapsed >= Math.Min(30, duration * _settings.ScrobbleThreshold))
        {
            _playRecorded = true;
            _library.RecordPlay(_currentTrack.Id);
        }
    }

    public void PlayPlaylist(Playlist playlist)
    {
        if (playlist.Tracks.Count == 0) return;
        _activePlaylist = playlist;
        _activePlaylistIndex = 0;
        PlayTrack(playlist.Tracks[0]);
    }

    public Track? CurrentTrack
    {
        get => _currentTrack;
        private set
        {
            _currentTrack = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentTrackDisplay));
            OnPropertyChanged(nameof(CurrentArtistDisplay));
            OnPropertyChanged(nameof(CurrentArtistOrNowPlayingDisplay));
            OnPropertyChanged(nameof(HasAlbumArt));
            OnPropertyChanged(nameof(AlbumArtPath));
            OnPropertyChanged(nameof(IsCurrentFavorite));
            OnPropertyChanged(nameof(IsAudiobook));
            OnPropertyChanged(nameof(IsRadio));
            OnPropertyChanged(nameof(Show10sSeek));
            OnPropertyChanged(nameof(ArtPlaceholderKind));
        }
    }

    public bool IsAudiobook => _currentTrack?.MediaType == MediaType.Audiobook;
    public bool IsRadio => _currentTrack?.MediaType == MediaType.Radio;
    public bool Show10sSeek => !IsAudiobook && !IsRadio;
    public bool IsShuffleNormal => _shuffleMode == ShuffleMode.Normal;
    public bool IsSmartShuffle => _shuffleMode == ShuffleMode.Smart;
    public bool IsRateAdjusted => Math.Abs(_playbackRate - 1.0f) > 0.001f;
    public MaterialIconKind ArtPlaceholderKind => _currentTrack?.MediaType switch
    {
        MediaType.Radio => MaterialIconKind.Radio,
        MediaType.Audiobook => MaterialIconKind.BookOpen,
        MediaType.Podcast => MaterialIconKind.Podcast,
        _ => MaterialIconKind.MusicNote
    };
    public IBrush PlaybackRateForeground => Math.Abs(_playbackRate - 1.0f) > 0.001f
        ? (IBrush)Application.Current!.Resources["BrushAccent"]!
        : (IBrush)Application.Current!.Resources["BrushTextSecondary"]!;

    public string CurrentTrackDisplay => _currentTrack == null ? L("Player_Status_NoTrack") : _currentTrack.Title;
    public string CurrentArtistDisplay => _currentTrack?.Artist ?? string.Empty;

    public string NowPlayingTitle
    {
        get => _nowPlayingTitle;
        private set
        {
            _nowPlayingTitle = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(NowPlayingDisplay));
            OnPropertyChanged(nameof(CurrentArtistOrNowPlayingDisplay));
            OnPropertyChanged(nameof(HasNowPlaying));
        }
    }

    public string NowPlayingArtist
    {
        get => _nowPlayingArtist;
        private set
        {
            _nowPlayingArtist = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(NowPlayingDisplay));
            OnPropertyChanged(nameof(CurrentArtistOrNowPlayingDisplay));
            OnPropertyChanged(nameof(HasNowPlaying));
        }
    }

    public bool HasNowPlaying => !string.IsNullOrWhiteSpace(_nowPlayingTitle) && _currentTrack?.MediaType == MediaType.Radio;

    public string NowPlayingDisplay
    {
        get
        {
            if (!HasNowPlaying) return string.Empty;
            return string.IsNullOrWhiteSpace(_nowPlayingArtist)
                ? _nowPlayingTitle
                : $"{_nowPlayingArtist} - {_nowPlayingTitle}";
        }
    }

    public string CurrentArtistOrNowPlayingDisplay
    {
        get
        {
            if (HasNowPlaying) return NowPlayingDisplay;
            return CurrentArtistDisplay;
        }
    }

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
            OnPropertyChanged(nameof(DurationDisplay));
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
            OnPropertyChanged(nameof(IsShuffleNormal));
            OnPropertyChanged(nameof(IsSmartShuffle));
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
        ShuffleMode.Normal => L("Player_Tooltip_ShuffleNormal"),
        ShuffleMode.Smart => L("Player_Tooltip_ShuffleSmart"),
        _ => L("Player_Tooltip_ShuffleOff")
    };

    public string RepeatTooltip => RepeatMode switch
    {
        RepeatMode.One => L("Player_Tooltip_RepeatOne"),
        RepeatMode.All => L("Player_Tooltip_RepeatAll"),
        _ => L("Player_Tooltip_RepeatOff")
    };

    public IBrush ShuffleForeground => _shuffleMode switch
    {
        ShuffleMode.Normal => (IBrush)Application.Current!.Resources["BrushAccent2"]!,
        ShuffleMode.Smart  => (IBrush)Application.Current!.Resources["BrushAmberDark"]!,
        _                  => (IBrush)Application.Current!.Resources["BrushPlayerIcon"]!
    };

    public IBrush RepeatForeground => IsRepeat
        ? (IBrush)Application.Current!.Resources["BrushAccent2"]!
        : (IBrush)Application.Current!.Resources["BrushPlayerIcon"]!;

    public bool IsCurrentFavorite => _currentTrack?.IsFavorite ?? false;

    public ICommand PlayPauseCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand PlayTrackCommand { get; }
    public ICommand DownloadTrackCommand { get; }
    public ICommand SeekBackwardCommand { get; }
    public ICommand SeekForwardCommand { get; }
    public ICommand SkipBack30Command { get; }
    public ICommand SkipForward30Command { get; }
    public ICommand PreviousTrackCommand { get; }
    public ICommand NextTrackCommand { get; }
    public ICommand CycleShuffleCommand { get; }
    public ICommand CycleRepeatCommand { get; }
    public ICommand ToggleMuteCommand { get; }
    public ICommand ToggleCurrentFavoriteCommand { get; }

    // NEW: SLEEP TIMER COMMANDS
    public ICommand SetSleepTimerCommand { get; }
    public ICommand CancelSleepTimerCommand { get; }

    public void PlayTrack(Track? track)
    {
        if (track == null) return;

        NowPlayingTitle = string.Empty;
        NowPlayingArtist = string.Empty;

        if (_activePlaylist != null)
        {
            int idx = -1;
            for (int i = 0; i < _activePlaylist.Tracks.Count; i++)
            {
                if (_activePlaylist.Tracks[i].Id == track.Id) 
                { 
                    idx = i; 
                    break; 
                }
            }
            
            if (idx >= 0) _activePlaylistIndex = idx;
            else _activePlaylist = null;
        }

        // FIX: Respect the suppress flag to prevent Previous oscillation
        var recordHistory = !_suppressHistoryRecord;
        _suppressHistoryRecord = false;

        // FIX: SaveAudiobookProgress must ALWAYS run when leaving a track, 
        // but history recording is conditional.
        if (_currentTrack != null && _currentTrack.Id != track.Id)
        {
            SaveAudiobookProgress();
            if (recordHistory) _navigator.RecordPlay(_currentTrack);
        }

        _hasTriggeredCrossfade = false;

        if (_currentTrack != null && track.Id == _currentTrack.Id && _state == PlaybackState.Playing)
        {
            ShowAlreadyPlayingToast = true;
            _ = Task.Run(async () => {
                await Task.Delay(1500);
                Avalonia.Threading.Dispatcher.UIThread.Post(() => ShowAlreadyPlayingToast = false);
            });
            return;
        }

        var contextChanged = _currentTrack == null
            || _currentTrack.MediaType != track.MediaType
            || (track.MediaType == MediaType.Audiobook
                && (_currentTrack.Album != track.Album || _currentTrack.Artist != track.Artist));

        _playSessionId = Guid.NewGuid();
        CurrentTrack = track;
        _navigator.CurrentTrack = track;
        if (contextChanged)
            _navigator.InvalidateDeck();
        AlbumArtPath = track.AlbumArtPath;
        _playRecorded = false;

        // LIVE RADIO: stream directly via LibVLC - never route a live stream
        // through the yt-dlp download pipeline (it would hang on an endless stream).
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

        if (!string.IsNullOrEmpty(track.FilePath) && System.IO.File.Exists(track.FilePath))
        {
            _pendingResumeTicks = track.MediaType == MediaType.Audiobook && _settings.AutoResumeAudiobooks
                ? track.PlaybackPositionTicks
                : 0;
            _playback.Play(track.FilePath);

            // Apply speed settings natively via LibVLC
            if (track.MediaType == MediaType.Audiobook) _playback.SetRate(_playbackRate); else _playback.SetRate(1.0f);

            if (track.MediaType == MediaType.Audiobook)
            {
                _audiobookSaveTimer?.Dispose();
                _audiobookSaveTimer = new System.Threading.Timer(_ => SaveAudiobookProgress(), null, 5000, 5000);
            }
            else
            {
                _audiobookSaveTimer?.Dispose();
                _audiobookSaveTimer = null;
            }

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
                _ = _download.DownloadAsync(track.Id.ToString(), track.Url, _settings.AudioFormat, _settings.AudioQuality);
            }
            else
            {
                StatusText = L("Player_Status_DownloadInProgress");
                Log.Debug("[{Source}] Skipped duplicate download for {Url}", nameof(PlayerViewModel), track.Url);
            }
            return;
        }

        Log.Warning("[PlayerViewModel] PlayTrack failed: FilePath='{FilePath}', Exists={Exists}, Url='{Url}'", track.FilePath, System.IO.File.Exists(track.FilePath), track.Url);
        StatusText = L("Player_Status_NoPlayableSource");
    }

    private void PlayPause()
    {
        if (IsPlaying)
        {
            SaveAudiobookProgress();
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
        SaveAudiobookProgress();
        _audiobookSaveTimer?.Dispose();
        _audiobookSaveTimer = null;
        _playback.Stop();
        if (_currentTrack != null)
            NullActionLogger.TrackStopped(_currentTrack.Id.ToString(), nameof(PlayerViewModel));
    }

    private void SaveAudiobookProgress()
    {
        if (_currentTrack?.MediaType != MediaType.Audiobook) return;
        var duration = _playback.Duration;
        var currentTicks = (long)(_position * duration.Ticks);
        if (currentTicks > 0 && currentTicks != _currentTrack.PlaybackPositionTicks)
        {
            _currentTrack.PlaybackPositionTicks = currentTicks;
            _library.Update(_currentTrack);
        }
    }

    private void OnTrackFinished()
    {
        if (_isCrossfading) return;

        if (_currentTrack != null)
        {
            if (_currentTrack.MediaType == MediaType.Audiobook)
            {
                _currentTrack.PlaybackPositionTicks = 0;
                _library.Update(_currentTrack);
            }

            if (!_playRecorded) _library.RecordPlay(_currentTrack.Id);
            NullActionLogger.TrackStopped(_currentTrack.Id.ToString(), nameof(PlayerViewModel));

            if (_position >= _settings.ScrobbleThreshold && _currentTrack.MediaType != MediaType.Audiobook)
            {
                var freshTrack = _library.GetAll().FirstOrDefault(t => t.Id == _currentTrack.Id);
                var scrobbleTitle = freshTrack?.Title ?? _currentTrack.Title;
                var scrobbleArtist = freshTrack?.Artist ?? _currentTrack.Artist;

                TrackScrobbleRequested?.Invoke(scrobbleTitle, scrobbleArtist, DateTime.UtcNow);
            }
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
        var duration = _playback.Duration.TotalSeconds;
        if (duration > 0 && Position * duration > 3.0)
        {
            SeekTo(0f);
            return;
        }

        _download.CancelCurrentDownload();
        IsDownloading = false;
        if (_currentTrack?.MediaType == MediaType.Audiobook)
        {
            var prevChapter = _navigator.GetPreviousChapter(_currentTrack);
            if (prevChapter != null) { PlayTrack(prevChapter); return; }
        }

        if (_activePlaylist != null && _activePlaylistIndex > 0)
        {
            _activePlaylistIndex--;
            PlayTrack(_activePlaylist.Tracks[_activePlaylistIndex]);
            return;
        }

        _activePlaylist = null;
        var prev = _navigator.GetPreviousTrack(_currentTrack);
        
        // FIX: Suppress history recording for the track we are leaving when going backwards
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

        if (_activePlaylist != null)
        {
            if (IsShuffle)
            {
                var candidates = _activePlaylist.Tracks.Where(t => t.Id != _currentTrack?.Id).ToList();
                if (candidates.Count > 0)
                {
                    var pick = candidates[Random.Shared.Next(candidates.Count)];
                    _activePlaylistIndex = _activePlaylist.Tracks.IndexOf(pick);
                    PlayTrack(pick);
                    return;
                }
            }
            else if (_activePlaylistIndex < _activePlaylist.Tracks.Count - 1)
            {
                _activePlaylistIndex++;
                PlayTrack(_activePlaylist.Tracks[_activePlaylistIndex]);
                return;
            }

            _activePlaylist = null;
        }

        var queued = _library.DequeueNext();
        if (queued != null)
        {
            PlayTrack(queued);
            RefillAutoQueue();
            return;
        }

        _activePlaylist = null;
        var next = _navigator.GetNextTrack(_currentTrack);
        if (next != null) PlayTrack(next);
        else StatusText = L("Player_Status_EndOfLibrary");
    }

    private void RecordSkipIfEarly()
    {
        if (_currentTrack?.MediaType is MediaType.Radio or MediaType.Audiobook) return;
        if (_currentTrack == null) return;
        if (_trackStartTime == DateTime.MinValue) return;

        var elapsed = (DateTime.UtcNow - _trackStartTime).TotalSeconds;
        var window  = _settings.SkipPenaltyWindowSeconds;

        if (elapsed < 0.5) return;

        if (elapsed <= window)
        {
            if (_currentTrack.LastSkipped.HasValue && _currentTrack.LastSkipped.Value != DateTime.MinValue)
            {
                var daysSinceLastSkip = (DateTime.UtcNow - _currentTrack.LastSkipped.Value).TotalDays;
                var decayFactor = Math.Pow(0.5, daysSinceLastSkip);
                _currentTrack.SkipCount = (int)Math.Round(_currentTrack.SkipCount * decayFactor);
            }

            _currentTrack.SkipCount++;
            _currentTrack.LastSkipped = DateTime.UtcNow;
            _library.Update(_currentTrack);

            Log.Information("[Player] Skip penalty recorded for '{Title}' (skipped after {Elapsed:F1}s, decayed total skips: {Count})",
                _currentTrack.Title, elapsed, _currentTrack.SkipCount);

            NullActionLogger.User("SkipPenalty",
                $"track={_currentTrack.Id} elapsed={elapsed:F1}s skips={_currentTrack.SkipCount}",
                nameof(PlayerViewModel));
        }
    }
}