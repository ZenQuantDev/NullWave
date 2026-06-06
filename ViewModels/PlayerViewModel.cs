using System;
using System.Linq;
using System.Windows.Input;
using NullWave.Helpers;
using NullWave.Helpers.Logging;
using NullWave.Models;
using NullWave.Services;
using NullWave.ViewModels.Base;
using Serilog;

namespace NullWave.ViewModels;

public class PlayerViewModel : ViewModelBase
{
    private readonly PlaybackService _playback;
    private readonly DownloadService _download;
    private readonly LibraryService  _library;
    private Track?        _currentTrack;
    private PlaybackState _state         = PlaybackState.Stopped;
    private float         _position;
    private float         _volume        = 0.8f;
    private bool          _isDownloading;
    private float         _downloadProgress;
    private string        _statusText    = "No track playing";

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
        }
    }

    public string CurrentTrackDisplay => _currentTrack == null
        ? "No track playing"
        : $"{_currentTrack.Artist} — {_currentTrack.Title}";

    private string? _albumArtPath;
    public string? AlbumArtPath
    {
        get => _albumArtPath;
        set { _albumArtPath = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasAlbumArt)); }
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
            OnPropertyChanged(nameof(PlayPauseIcon));
        }
    }

    public bool   IsPlaying    => _state == PlaybackState.Playing;
    public string PlayPauseIcon => IsPlaying ? "⏸" : "▶";

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
            var total   = _playback.Duration;
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

    // Commands
    public ICommand PlayPauseCommand      { get; }
    public ICommand StopCommand           { get; }
    public ICommand PlayTrackCommand      { get; }
    public ICommand DownloadTrackCommand  { get; }
    public ICommand SeekBackwardCommand   { get; }
    public ICommand SeekForwardCommand    { get; }
    public ICommand PreviousTrackCommand  { get; }
    public ICommand NextTrackCommand      { get; }

    public PlayerViewModel(
        PlaybackService playback,
        DownloadService download,
        LibraryService  library)
    {
        _playback = playback;
        _download = download;
        _library  = library;
        _playback.Volume = _volume;

        _playback.PositionChanged += pos => Position = pos;
        _playback.StateChanged += state => State = state;
        _playback.TrackFinished += () =>
        {
            StatusText = "Finished";
            if (_currentTrack != null)
            {
                _library.RecordPlay(_currentTrack.Id);
                NullActionLogger.TrackStopped(_currentTrack.Id.ToString(), nameof(PlayerViewModel));
            }
        };

        _download.ProgressChanged += (_, pct) =>
        {
            DownloadProgress = pct;
            StatusText = $"Downloading... {pct:P0}";
        };

        _download.DownloadCompleted += (trackId, filePath) =>
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
                    PlayTrack(track);
                }
            }
        };

        _download.DownloadFailed += (url, error) =>
        {
            IsDownloading = false;
            StatusText = $"Download failed: {error}";
            NullActionLogger.Error(nameof(PlayerViewModel), $"Download failed: {error}", $"url={url}");
        };

        PlayPauseCommand     = new RelayCommand(PlayPause);
        StopCommand          = new RelayCommand(Stop);
        PlayTrackCommand     = new RelayCommand<Track>(PlayTrack);
        DownloadTrackCommand = new RelayCommand<Track>(async t =>
        {
            if (t?.Url == null) return;
            IsDownloading = true;
            StatusText = "Starting download...";
            NullActionLogger.ImportStarted(t.Url, nameof(PlayerViewModel));
            await _download.DownloadAsync(t.Id.ToString(), t.Url);
        });

        // NEW: Seek commands
        SeekBackwardCommand = new RelayCommand(() => SeekRelative(-5));
        SeekForwardCommand  = new RelayCommand(() => SeekRelative(5));
        PreviousTrackCommand = new RelayCommand(PlayPrevious);
        NextTrackCommand     = new RelayCommand(PlayNext);
    }

    public void PlayTrack(Track? track)
    {
        if (track == null) return;
        CurrentTrack = track;
        AlbumArtPath = null;

        if (!string.IsNullOrEmpty(track.FilePath) && System.IO.File.Exists(track.FilePath))
        {
            _playback.Play(track.FilePath);
            StatusText = CurrentTrackDisplay;
            NullActionLogger.TrackPlayed(track.Id.ToString(), track.Title, track.Artist, nameof(PlayerViewModel));
            return;
        }

        if (!string.IsNullOrEmpty(track.Url))
        {
            IsDownloading = true;
            StatusText = "Downloading before playback...";
            NullActionLogger.ImportStarted(track.Url, nameof(PlayerViewModel));
            _ = _download.DownloadAsync(track.Id.ToString(), track.Url);
            return;
        }

        StatusText = "No playable source found";
    }

    private void PlayPause()
    {
        if (IsPlaying)
        {
            _playback.Pause();
            NullActionLogger.TrackPaused(_currentTrack?.Id.ToString() ?? "?", PositionDisplay, nameof(PlayerViewModel));
        }
        else if (_state == PlaybackState.Paused)
        {
            _playback.Resume();
            if (_currentTrack != null)
                NullActionLogger.TrackPlayed(_currentTrack.Id.ToString(), _currentTrack.Title, _currentTrack.Artist, nameof(PlayerViewModel));
        }
        else if (_currentTrack != null)
        {
            PlayTrack(_currentTrack);
        }
    }

    private void Stop()
    {
        _playback.Stop();
        if (_currentTrack != null)
            NullActionLogger.TrackStopped(_currentTrack.Id.ToString(), nameof(PlayerViewModel));
    }

    // NEW: Seek relative (seconds)
    private void SeekRelative(int seconds)
    {
        var duration = _playback.Duration.TotalSeconds;
        if (duration <= 0) return;
        
        var currentPosition = Position * duration;
        var newPosition = Math.Clamp(currentPosition + seconds, 0, duration);
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
        // Convert to List so we can use FindIndex
        var queue = _library.GetQueue().ToList();
        if (queue.Count == 0 || _currentTrack == null) return;
        
        var currentIndex = queue.FindIndex(t => t.Id == _currentTrack.Id);
        if (currentIndex > 0)
            PlayTrack(queue[currentIndex - 1]);
    }

    private void PlayNext()
    {
        // Convert to List so we can use FindIndex
        var queue = _library.GetQueue().ToList();
        if (queue.Count == 0) return;
        
        if (_currentTrack == null)
        {
            PlayTrack(queue[0]);
            return;
        }
        
        var currentIndex = queue.FindIndex(t => t.Id == _currentTrack.Id);
        if (currentIndex < queue.Count - 1)
            PlayTrack(queue[currentIndex + 1]);
    }
}