using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using NullWave.Helpers;
using NullWave.Helpers.Logging;
using NullWave.Models;
using NullWave.Services;
using NullWave.ViewModels.Base;
using Serilog;

namespace NullWave.ViewModels;

public class PlaylistImportViewModel : ViewModelBase
{
    private readonly LibraryService _library;
    private readonly MetadataService _metadata;
    private readonly DownloadService _download;

    private CancellationTokenSource? _cts;
    private bool _isImporting;
    private int _progress;
    private int _total;
    private string _currentTrack = string.Empty;

    public bool IsImporting
    {
        get => _isImporting;
        private set { _isImporting = value; OnPropertyChanged(); }
    }

    public int Progress
    {
        get => _progress;
        private set { _progress = value; OnPropertyChanged(); }
    }

    public int Total
    {
        get => _total;
        private set { _total = value; OnPropertyChanged(); }
    }

    public string CurrentTrack
    {
        get => _currentTrack;
        private set { _currentTrack = value; OnPropertyChanged(); }
    }

    public ICommand CancelCommand { get; }

    public PlaylistImportViewModel(
        LibraryService library,
        MetadataService metadata,
        DownloadService download)
    {
        _library = library;
        _metadata = metadata;
        _download = download;

        CancelCommand = new RelayCommand(Cancel);
    }

    public void Cancel()
    {
        _cts?.Cancel();
        IsImporting = false;
        Log.Information("[{Source}] Playlist import cancelled by user", nameof(PlaylistImportViewModel));
    }

    public void ImportPlaylist(string playlistUrl)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        IsImporting = true;
        Progress = 0;
        Total = 0;
        CurrentTrack = "Fetching playlist...";

        _ = _download.DownloadPlaylistAsync(
            playlistUrl,
            onTrackStarted: (title, index, total) =>
            {
                Progress = index;
                Total = total;
                CurrentTrack = title;
                Log.Information("[{Source}] Playlist track started: {Title} ({Index}/{Total})",
                    nameof(PlaylistImportViewModel), title, index, total);
            },
            onTrackCompleted: (title, artist, filePath) =>
            {
                var track = new Track
                {
                    Title = title,
                    Artist = artist,
                    FilePath = filePath,
                    Source = TrackSource.YouTube,
                    AlbumArtPath = _metadata.ExtractAlbumArt(filePath)
                };
                _library.Add(track);
                NullActionLogger.TrackAdded(track.Id.ToString(), filePath, nameof(PlaylistImportViewModel));
            },
            onTrackFailed: (title, error) =>
            {
                NullActionLogger.Error(nameof(PlaylistImportViewModel),
                    $"Playlist track failed: {title} - {error}", playlistUrl);
            },
            ct: ct);

        // Monitor completion
        _ = Task.Run(async () =>
        {
            while (IsImporting && !ct.IsCancellationRequested)
            {
                if (Total > 0 && Progress >= Total)
                {
                    IsImporting = false;
                    CurrentTrack = string.Empty;
                    Log.Information("[{Source}] Playlist import complete: {Total} tracks",
                        nameof(PlaylistImportViewModel), Total);
                    break;
                }
                await Task.Delay(500, ct).ContinueWith(_ => { });
            }
        });
    }
}