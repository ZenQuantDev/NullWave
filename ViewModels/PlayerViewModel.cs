using System;
using System.Windows.Input;
using NullWave.Helpers;
using NullWave.Models;
using NullWave.Services;
using NullWave.ViewModels.Base;
using Serilog;

namespace NullWave.ViewModels;

public class PlayerViewModel : ViewModelBase
{
    private readonly PlaybackService _playback;
    private readonly DownloadService _download;
    private readonly LibraryService _library;

    private Track? _currentTrack;
    private PlaybackState _state = PlaybackState.Stopped;
    private float _position;
    private float _volume = 0.8f;
    private bool _isDownloading;
    private float _downloadProgress;
    private string _statusText = "No track playing";

    public Track? CurrentTrack
    {
        get => _currentTrack;
        private set
        {
            _currentTrack = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentTrackDisplay));
        }
    }

    public string CurrentTrackDisplay => _currentTrack == null
        ? "No track playing"
        : string.IsNullOrWhiteSpace(_currentTrack.Artist)
            ? _currentTrack.Title
            : $"{_currentTrack.Artist} — {_currentTrack.Title}";

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

    public bool IsPlaying => _state == PlaybackState.Playing;
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
            var total = _playback.Duration;
            var current = TimeSpan.FromSeconds(Position * total.TotalSeconds);
            return $"{current:mm\\:ss} / {total:mm\\:ss}";
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

    public ICommand PlayPauseCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand PlayTrackCommand { get; }
    public ICommand DownloadTrackCommand { get; }

    public PlayerViewModel(
        PlaybackService playback,
        DownloadService download,
        LibraryService library)
    {
        _playback = playback;
        _download = download;
        _library = library;

        _playback.Volume = _volume;

        _playback.PositionChanged += pos =>
        {
            Position = pos;
        };

        _playback.StateChanged += state =>
        {
            State = state;
        };

        _playback.TrackFinished += () =>
        {
            StatusText = "Finished";
            if (_currentTrack != null)
                _library.RecordPlay(_currentTrack.Id);
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
            Log.Information("Download complete, updating track path: {Path}", filePath);

            // Update the track's FilePath in the library
            if (Guid.TryParse(trackId, out var id))
            {
                var track = _library.GetAll()
                    .FirstOrDefault(t => t.Id == id);
                if (track != null)
                {
                    track.FilePath = filePath;
                    PlayTrack(track);
                }
            }
        };

        _download.DownloadFailed += (_, error) =>
        {
            IsDownloading = false;
            StatusText = $"Download failed: {error}";
            Log.Error("Download failed: {Error}", error);
        };

        PlayPauseCommand = new RelayCommand(PlayPause);
        StopCommand = new RelayCommand(_playback.Stop);
        PlayTrackCommand = new RelayCommand<Track>(PlayTrack);
        DownloadTrackCommand = new RelayCommand<Track>(async t =>
        {
            if (t?.Url == null) return;
            IsDownloading = true;
            StatusText = "Starting download...";
            await _download.DownloadAsync(t.Id.ToString(), t.Url);
        });
    }

    public void PlayTrack(Track? track)
    {
        if (track == null) return;

        CurrentTrack = track;

        // Local file — play directly
        if (!string.IsNullOrEmpty(track.FilePath) &&
            System.IO.File.Exists(track.FilePath))
        {
            _playback.Play(track.FilePath);
            StatusText = CurrentTrackDisplay;
            return;
        }

        // Has URL — download first then play
        if (!string.IsNullOrEmpty(track.Url))
        {
            IsDownloading = true;
            StatusText = "Downloading before playback...";
            _ = _download.DownloadAsync(track.Id.ToString(), track.Url);
            return;
        }

        StatusText = "No playable source found";
    }

    private void PlayPause()
    {
        if (IsPlaying)
            _playback.Pause();
        else if (_state == PlaybackState.Paused)
            _playback.Resume();
        else if (_currentTrack != null)
            PlayTrack(_currentTrack);
    }
}