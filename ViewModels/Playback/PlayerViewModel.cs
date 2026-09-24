using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;
using Serilog;
using NullWave.Models;
using NullWave.Services;
using NullWave.Helpers;
using NullWave.Helpers.Logging;
using NullWave.ViewModels.Base;
using NullWave.Services.Metadata;

namespace NullWave.ViewModels;

public enum ShuffleMode { Off, Normal, Smart }

public partial class PlayerViewModel : ViewModelBase
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
    private bool _suppressHistoryRecord;
    private System.Threading.Timer? _sleepTimer;
    private TimeSpan _sleepTimerRemaining;
    private bool _sleepTimerEndOfChapter;
    private Playlist? _activePlaylist;
    private int _activePlaylistIndex = -1;
    private float _volumeBeforeMute = 0.8f;
    private bool _isMuted;
    private ShuffleMode _shuffleMode = ShuffleMode.Off;
    private string _nowPlayingTitle = string.Empty;
    private string _nowPlayingArtist = string.Empty;
    private float _playbackRate = 1.0f;
    private DateTime _lastNavigationTime = DateTime.MinValue;
    private static readonly TimeSpan NavigationDebounce = TimeSpan.FromMilliseconds(300);
    private static string L(string key) => LocalizationService.Instance[key];

    public bool SleepTimerEndOfChapter { get => _sleepTimerEndOfChapter; set { _sleepTimerEndOfChapter = value; OnPropertyChanged(); } }
    public float PlaybackRate
    {
        get => _playbackRate;
        set { _playbackRate = value; _playback.SetRate(value); OnPropertyChanged(); OnPropertyChanged(nameof(PlaybackRateDisplay)); OnPropertyChanged(nameof(PlaybackRateForeground)); OnPropertyChanged(nameof(IsRateAdjusted)); _settings.AudiobookPlaybackRate = value; }
    }
    public string PlaybackRateDisplay => $"{_playbackRate:0.0#}x";
    public event Action<string, string, DateTime>? TrackScrobbleRequested;
    public event Action? PlaySelectedTrackRequested;

    public PlayerViewModel(PlaybackService playback, DownloadService download, LibraryService library, SettingsViewModel settings, MetadataService metadata)
    {
        _playback = playback; _download = download; _library = library; _settings = settings; _metadata = metadata;
        _navigator = new PlaybackNavigator(library);
        _playbackRate = _settings.AudiobookPlaybackRate;
        _playback.Volume = _volume;

        _playback.PositionChanged += pos => Dispatcher.UIThread.Post(() => { Position = pos; CheckPlayRecorded(); CheckCrossfade(pos); });
        _playback.StateChanged += state => Dispatcher.UIThread.Post(() => { State = state; if (state == PlaybackState.Playing && _pendingResumeTicks > 0 && _playback.Duration > TimeSpan.Zero) { var frac = (float)Math.Clamp(_pendingResumeTicks / (double)_playback.Duration.Ticks, 0, 0.98); _playback.Seek(frac); _pendingResumeTicks = 0; } if (state is PlaybackState.Paused or PlaybackState.Stopped) SaveAudiobookProgress(); });
        _playback.TrackFinished += () => Dispatcher.UIThread.Post(() => { if (_currentTrack?.MediaType == MediaType.Radio) return; if (_sleepTimerEndOfChapter && _currentTrack?.MediaType == MediaType.Audiobook) { SleepTimerEndOfChapter = false; _ = _playback.FadeAndPauseAsync(1500).ContinueWith(_ => Dispatcher.UIThread.Post(() => { Stop(); ToastService.Instance.Show(L("MiniPlayer_SleepTimer_ChapterStopped"), ToastType.Info, scope: "sleep"); })); return; } OnTrackFinished(); });
        _playback.DurationDiscovered += duration => Dispatcher.UIThread.Post(() => { var track = CurrentTrack; if (track != null && track.Duration == TimeSpan.Zero && duration > TimeSpan.Zero) { track.Duration = duration; _library.Update(track); } });
        _playback.RadioMetadataChanged += (title, artist) => Dispatcher.UIThread.Post(() => { if (_currentTrack?.MediaType == MediaType.Radio) StatusText = string.IsNullOrWhiteSpace(artist) ? title : $"{artist} - {title}"; });
        _playback.StreamFailed += reason => Dispatcher.UIThread.Post(() => { if (_currentTrack?.MediaType != MediaType.Radio) return; StatusText = $"{_currentTrack.Title} - stream unavailable"; ToastService.Instance.Show($"'{_currentTrack.Title}' stream is unreachable. Try another station.", ToastType.Warning, scope: "radio"); });
        
        _download.ProgressChanged += (_, pct) => Dispatcher.UIThread.Post(() => { DownloadProgress = pct; StatusText = string.Format(L("Player_Status_DownloadingPct"), pct); });
        
        _download.DownloadCompleted += (trackId, filePath, isInteractive) =>
        {
            if (!isInteractive || string.IsNullOrEmpty(filePath)) return;

            Dispatcher.UIThread.Post(() =>
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
                            // FIX: embedded title may be "Artist - Title"; split it so the
                            // renamed file doesn't double the artist prefix.
                            var parsedTags = TrackTitleParser.TryParseArtistTitle(tagTitle);
                            var cleanTitle  = tagTitle;
                            var cleanArtist = tagArtist;
                            if (parsedTags != null && !string.IsNullOrWhiteSpace(parsedTags.Value.Title))
                            {
                                cleanTitle = parsedTags.Value.Title;
                                if (!string.IsNullOrWhiteSpace(parsedTags.Value.Artist))
                                    cleanArtist = parsedTags.Value.Artist;
                            }

                            if (TrackTitleParser.IsPlaceholderTitle(track.Title))  track.Title  = cleanTitle;
                            if (TrackTitleParser.IsPlaceholderArtist(track.Artist) &&
                                !string.IsNullOrWhiteSpace(cleanArtist))          track.Artist = cleanArtist;

                            _download.UpdateJobMetadata(trackId, track.Title, track.Artist);
                        }

                        track.Duration = duration;
                        _library.Update(track);
                        _library.NormalizeLocalFile(track);

                        var fresh = _library.GetAll().FirstOrDefault(t => t.Id == id);
                        PlayTrack(fresh ?? track);
                    }
                }
            });
        };

        _download.DownloadFailed += (trackId, error, isInteractive) => { if (!isInteractive) return; Dispatcher.UIThread.Post(() => { IsDownloading = false; StatusText = string.Format(L("Player_Status_DownloadFailed"), error ?? L("Player_Status_UnknownError")); NullActionLogger.Error(nameof(PlayerViewModel), $"Download failed: {error ?? "Unknown error"}", $"trackId={trackId}"); if (Guid.TryParse(trackId, out var id)) { var failedTrack = _library.GetAll().FirstOrDefault(t => t.Id == id); if (failedTrack != null) { var retryTarget = failedTrack; ToastService.Instance.Show(message: $"Failed to download '{retryTarget?.Title ?? "Unknown Track"}'", type: ToastType.Error, durationMs: 6000, detailedMessage: error ?? "An unknown error occurred during download.", actionText: "Retry", actionCallback: () => { if (retryTarget != null) DownloadTrackCommand?.Execute(retryTarget); }, scope: "download-fail"); } } }); };

        InitializeCommands();
    }

    public Track? CurrentTrack
    {
        get => _currentTrack;
        private set
        {
            _currentTrack = value; OnPropertyChanged(); OnPropertyChanged(nameof(CurrentTrackDisplay)); OnPropertyChanged(nameof(CurrentArtistDisplay));
            OnPropertyChanged(nameof(CurrentArtistOrNowPlayingDisplay)); OnPropertyChanged(nameof(HasAlbumArt)); OnPropertyChanged(nameof(AlbumArtPath));
            OnPropertyChanged(nameof(IsCurrentFavorite)); OnPropertyChanged(nameof(IsAudiobook)); OnPropertyChanged(nameof(IsRadio));
            OnPropertyChanged(nameof(Show10sSeek)); OnPropertyChanged(nameof(ArtPlaceholderKind));
        }
    }

    public bool IsAudiobook => _currentTrack?.MediaType == MediaType.Audiobook;
    public bool IsRadio => _currentTrack?.MediaType == MediaType.Radio;
    public bool Show10sSeek => !IsAudiobook && !IsRadio;
    public bool IsShuffleNormal => _shuffleMode == ShuffleMode.Normal;
    public bool IsSmartShuffle => _shuffleMode == ShuffleMode.Smart;
    public bool IsRateAdjusted => Math.Abs(_playbackRate - 1.0f) > 0.001f;
    public MaterialIconKind ArtPlaceholderKind => _currentTrack?.MediaType switch { MediaType.Radio => MaterialIconKind.Radio, MediaType.Audiobook => MaterialIconKind.BookOpen, MediaType.Podcast => MaterialIconKind.Podcast, _ => MaterialIconKind.MusicNote };
    public IBrush PlaybackRateForeground => Math.Abs(_playbackRate - 1.0f) > 0.001f ? (IBrush)Application.Current!.Resources["BrushAccent"]! : (IBrush)Application.Current!.Resources["BrushTextSecondary"]!;
    public string CurrentTrackDisplay => _currentTrack == null ? L("Player_Status_NoTrack") : _currentTrack.Title;
    public string CurrentArtistDisplay => _currentTrack?.Artist ?? string.Empty;
    
    public string NowPlayingTitle { get => _nowPlayingTitle; private set { _nowPlayingTitle = value; OnPropertyChanged(); OnPropertyChanged(nameof(NowPlayingDisplay)); OnPropertyChanged(nameof(CurrentArtistOrNowPlayingDisplay)); OnPropertyChanged(nameof(HasNowPlaying)); } }
    public string NowPlayingArtist { get => _nowPlayingArtist; private set { _nowPlayingArtist = value; OnPropertyChanged(); OnPropertyChanged(nameof(NowPlayingDisplay)); OnPropertyChanged(nameof(CurrentArtistOrNowPlayingDisplay)); OnPropertyChanged(nameof(HasNowPlaying)); } }
    public bool HasNowPlaying => !string.IsNullOrWhiteSpace(_nowPlayingTitle) && _currentTrack?.MediaType == MediaType.Radio;
    public string NowPlayingDisplay => !HasNowPlaying ? string.Empty : (string.IsNullOrWhiteSpace(_nowPlayingArtist) ? _nowPlayingTitle : $"{_nowPlayingArtist} - {_nowPlayingTitle}");
    public string CurrentArtistOrNowPlayingDisplay => HasNowPlaying ? NowPlayingDisplay : CurrentArtistDisplay;
    
    private string? _albumArtPath;
    public string? AlbumArtPath { get => _albumArtPath; set { _albumArtPath = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasAlbumArt)); } }
    public bool HasAlbumArt => !string.IsNullOrEmpty(_albumArtPath);
    
    public PlaybackState State { get => _state; private set { _state = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsPlaying)); OnPropertyChanged(nameof(PlayPauseIconKind)); } }
    public bool IsPlaying => _state == PlaybackState.Playing;
    public MaterialIconKind PlayPauseIconKind => IsPlaying ? MaterialIconKind.Pause : MaterialIconKind.Play;
    
    public float Position { get => _position; set { _position = value; OnPropertyChanged(); OnPropertyChanged(nameof(PositionDisplay)); OnPropertyChanged(nameof(DurationDisplay)); } }
    public string PositionDisplay { get { var total = _playback.Duration; var current = TimeSpan.FromSeconds(Position * total.TotalSeconds); return $"{(int)current.TotalMinutes:D2}:{current.Seconds:D2}"; } }
    public string DurationDisplay { get { var total = _playback.Duration; return $"{(int)total.TotalMinutes:D2}:{total.Seconds:D2}"; } }
    
    public float Volume { get => _volume; set { _volume = value; _playback.Volume = value; OnPropertyChanged(); OnPropertyChanged(nameof(VolumeIconKind)); } }
    public bool IsDownloading { get => _isDownloading; private set { _isDownloading = value; OnPropertyChanged(); } }
    public float DownloadProgress { get => _downloadProgress; private set { _downloadProgress = value; OnPropertyChanged(); } }
    public string StatusText { get => _statusText; private set { _statusText = value; OnPropertyChanged(); } }
    
    private bool _showAlreadyPlayingToast;
    public bool ShowAlreadyPlayingToast { get => _showAlreadyPlayingToast; private set { _showAlreadyPlayingToast = value; OnPropertyChanged(); } }
    
    public ShuffleMode ShuffleMode
    {
        get => _shuffleMode;
        private set { _shuffleMode = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsShuffle)); OnPropertyChanged(nameof(IsShuffleNormal)); OnPropertyChanged(nameof(IsSmartShuffle)); OnPropertyChanged(nameof(ShuffleIconKind)); OnPropertyChanged(nameof(ShuffleTooltip)); OnPropertyChanged(nameof(ShuffleForeground)); }
    }
    public bool IsShuffle => _shuffleMode != ShuffleMode.Off;
    public RepeatMode RepeatMode => _navigator.RepeatMode;
    public bool IsRepeat => _navigator.RepeatMode != RepeatMode.None;
    public MaterialIconKind ShuffleIconKind => _shuffleMode == ShuffleMode.Smart ? MaterialIconKind.AutoFixHigh : MaterialIconKind.Shuffle;
    public MaterialIconKind RepeatIconKind => RepeatMode switch { RepeatMode.One => MaterialIconKind.RepeatOnce, RepeatMode.All => MaterialIconKind.Repeat, _ => MaterialIconKind.RepeatOff };
    public MaterialIconKind VolumeIconKind => _isMuted || _volume == 0 ? MaterialIconKind.VolumeMute : (_volume < 0.5f ? MaterialIconKind.VolumeLow : MaterialIconKind.VolumeHigh);
    public string ShuffleTooltip => _shuffleMode switch { ShuffleMode.Normal => L("Player_Tooltip_ShuffleNormal"), ShuffleMode.Smart => L("Player_Tooltip_ShuffleSmart"), _ => L("Player_Tooltip_ShuffleOff") };
    public string RepeatTooltip => RepeatMode switch { RepeatMode.One => L("Player_Tooltip_RepeatOne"), RepeatMode.All => L("Player_Tooltip_RepeatAll"), _ => L("Player_Tooltip_RepeatOff") };
    public IBrush ShuffleForeground => _shuffleMode switch { ShuffleMode.Normal => (IBrush)Application.Current!.Resources["BrushAccent2"]!, ShuffleMode.Smart => (IBrush)Application.Current!.Resources["BrushAmberDark"]!, _ => (IBrush)Application.Current!.Resources["BrushPlayerIcon"]! };
    public IBrush RepeatForeground => IsRepeat ? (IBrush)Application.Current!.Resources["BrushAccent2"]! : (IBrush)Application.Current!.Resources["BrushPlayerIcon"]!;
    public bool IsCurrentFavorite => _currentTrack?.IsFavorite ?? false;

    public ICommand PlayPauseCommand { get; private set; } = null!;
    public ICommand StopCommand { get; private set; } = null!;
    public ICommand PlayTrackCommand { get; private set; } = null!;
    public ICommand DownloadTrackCommand { get; private set; } = null!;
    public ICommand SeekBackwardCommand { get; private set; } = null!;
    public ICommand SeekForwardCommand { get; private set; } = null!;
    public ICommand SkipBack30Command { get; private set; } = null!;
    public ICommand SkipForward30Command { get; private set; } = null!;
    public ICommand PreviousTrackCommand { get; private set; } = null!;
    public ICommand NextTrackCommand { get; private set; } = null!;
    public ICommand CycleShuffleCommand { get; private set; } = null!;
    public ICommand CycleRepeatCommand { get; private set; } = null!;
    public ICommand ToggleMuteCommand { get; private set; } = null!;
    public ICommand ToggleCurrentFavoriteCommand { get; private set; } = null!;
    public ICommand SetSleepTimerCommand { get; private set; } = null!;
    public ICommand CancelSleepTimerCommand { get; private set; } = null!;
    public ICommand SetEndOfChapterTimerCommand { get; private set; } = null!;
    public ICommand SetPlaybackRateCommand { get; private set; } = null!;

    public TimeSpan SleepTimerRemaining
    {
        get => _sleepTimerRemaining;
        private set { _sleepTimerRemaining = value; OnPropertyChanged(); OnPropertyChanged(nameof(SleepTimerDisplay)); OnPropertyChanged(nameof(IsSleepTimerActive)); }
    }
    public bool IsSleepTimerActive => _sleepTimerRemaining > TimeSpan.Zero;
    public string SleepTimerDisplay => IsSleepTimerActive ? $"{(int)_sleepTimerRemaining.TotalMinutes:D2}:{_sleepTimerRemaining.Seconds:D2}" : "";
}