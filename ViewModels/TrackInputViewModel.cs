using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using NullWave.Helpers;
using NullWave.Helpers.Logging;
using NullWave.Models;
using NullWave.Services;
using NullWave.ViewModels.Base;
using Serilog;

namespace NullWave.ViewModels;

public class TrackInputViewModel : ViewModelBase
{
    private readonly LibraryService _library;
    private readonly MetadataService _metadata;
    private readonly UrlParserService _urlParser;
    private readonly DownloadService _download;
    private readonly SpotifyBridgeService _spotifyBridge;
    private readonly SettingsViewModel _settings;

    private string _inputUrl = string.Empty;
    private string _lastFetchedUrl = string.Empty;
    private string _inputTitle = string.Empty;
    private string _inputArtist = string.Empty;
    private TrackSource _selectedSource = TrackSource.Unknown;
    private bool _isFetching;
    private bool _isUrlInputVisible;
    private string _statusMessage = string.Empty;

    public Array SourceOptions => Enum.GetValues(typeof(TrackSource));
    public ICommand AddTrackCommand { get; }
    public ICommand AddLocalFileCommand { get; }
    public ICommand ShowUrlInputCommand { get; }

    public event Action? TrackAdded;

    public PlaylistImportViewModel PlaylistImport { get; }

    public bool IsFetching
    {
        get => _isFetching;
        set { _isFetching = value; OnPropertyChanged(); }
    }

    public bool IsUrlInputVisible
    {
        get => _isUrlInputVisible;
        set { _isUrlInputVisible = value; OnPropertyChanged(); }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    public string InputUrl
    {
        get => _inputUrl;
        set
        {
            _inputUrl = value;
            OnPropertyChanged();
            SelectedSource = SourceDetector.Detect(value);

            if (_urlParser.IsValidUrl(value) && value != _lastFetchedUrl)
            {
                _lastFetchedUrl = value;
                _ = FetchMetadataAsync(value);
            }
        }
    }

    public string InputTitle
    {
        get => _inputTitle;
        set { _inputTitle = value; OnPropertyChanged(); }
    }

    public string InputArtist
    {
        get => _inputArtist;
        set { _inputArtist = value; OnPropertyChanged(); }
    }

    public TrackSource SelectedSource
    {
        get => _selectedSource;
        set { _selectedSource = value; OnPropertyChanged(); }
    }

    public TrackInputViewModel(
        LibraryService library,
        MetadataService metadata,
        UrlParserService urlParser,
        DownloadService download,
        SpotifyBridgeService spotifyBridge,
        SettingsViewModel settings,
        PlaylistImportViewModel playlistImport)
    {
        _library = library;
        _metadata = metadata;
        _urlParser = urlParser;
        _download = download;
        _spotifyBridge = spotifyBridge;
        _settings = settings;
        PlaylistImport = playlistImport;

        AddTrackCommand = new RelayCommand(AddTrack);
        AddLocalFileCommand = new RelayCommand(async () => await AddLocalFileAsync());
        ShowUrlInputCommand = new RelayCommand(() => IsUrlInputVisible = !IsUrlInputVisible);
    }

    public void AddTrack()
    {
        var title = InputTitle.Trim();
        var url = InputUrl.Trim();

        if (string.IsNullOrWhiteSpace(url))
        {
            IsUrlInputVisible = true;
            return;
        }

        // Spotify → YouTube bridge
        if (SelectedSource == TrackSource.Spotify)
        {
            IsUrlInputVisible = false;
            ClearInputs();
            _ = HandleSpotifyUrlAsync(url);
            return;
        }

        // Auto-detect local file/folder path
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (System.IO.Directory.Exists(url))
            {
                _ = AddFolderPathAsync(url);
                return;
            }
            if (System.IO.File.Exists(url) && _urlParser.IsSupportedAudioFile(url))
            {
                var (t, a) = _metadata.FetchFromLocalFile(url);
                var track = new Track
                {
                    Title = string.IsNullOrWhiteSpace(title) ? t : title,
                    Artist = InputArtist.Trim().Length > 0 ? InputArtist.Trim() : a,
                    FilePath = url,
                    Source = TrackSource.Local
                };
                _library.Add(track);
                NullActionLogger.TrackAdded(track.Id.ToString(), url, nameof(TrackInputViewModel));
                ClearInputs();
                IsUrlInputVisible = false;
                TrackAdded?.Invoke();
                return;
            }
        }

        if (string.IsNullOrWhiteSpace(title))
            title = url;

        // Reject bare domain roots and unplayable URLs
        if (!SourceDetector.IsPlayableUrl(url))
        {
            StatusMessage = "That URL doesn't look like a playable track. " +
                            "Paste a direct video or track link.";
            Log.Warning("[{Source}] Rejected unplayable URL: {Url}",
                nameof(TrackInputViewModel), url);
            return;
        }

        var source = SelectedSource;
        var newTrack = new Track
        {
            Title = title,
            Artist = InputArtist.Trim(),
            Url = url,
            Source = source
        };

        _library.Add(newTrack);
        NullActionLogger.TrackAdded(newTrack.Id.ToString(), url, nameof(TrackInputViewModel));
        ClearInputs();
        IsUrlInputVisible = false;
        TrackAdded?.Invoke();

        // Auto-download for YouTube and SoundCloud
        if (source == TrackSource.YouTube || source == TrackSource.SoundCloud)
        {
            _ = Task.Run(async () =>
                await _download.DownloadAsync(
                    newTrack.Id.ToString(), url,
                    _settings.AudioFormat, _settings.AudioQuality));
        }
    }

    private async Task FetchMetadataAsync(string url)
    {
        IsFetching = true;
        try
        {
            var (title, artist) = await _metadata.FetchFromUrlAsync(url);
            if (string.IsNullOrWhiteSpace(InputTitle)) InputTitle = title;
            if (string.IsNullOrWhiteSpace(InputArtist)) InputArtist = artist;
            Log.Information("[{Source}] Metadata fetched: {Title} by {Artist}",
                nameof(TrackInputViewModel), title, artist);

            // Backfill the track in the library if it was already added
            var existing = _library.GetAll()
                .FirstOrDefault(t => t.Url == url);
            if (existing != null)
            {
                if (existing.Title == url || existing.Title == "Unknown Title"
                    || string.IsNullOrWhiteSpace(existing.Title))
                    existing.Title = title;
                if (existing.Artist == "Unknown" || string.IsNullOrWhiteSpace(existing.Artist))
                    existing.Artist = artist;
                _library.Update(existing);
                Log.Information("[{Source}] Backfilled track metadata: {Title} by {Artist}",
                    nameof(TrackInputViewModel), title, artist);
            }
        }
        catch (Exception ex)
        {
            NullActionLogger.Error(nameof(TrackInputViewModel), ex, $"Metadata fetch failed for {url}");
        }
        finally
        {
            IsFetching = false;
        }
    }

    private async Task AddLocalFileAsync()
    {
        var window = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow : null;
        if (window == null) return;

        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Audio File",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Audio Files")
                {
                    Patterns = new[] { "*.mp3", "*.flac", "*.wav", "*.ogg", "*.m4a", "*.aac" }
                }
            }
        });

        if (files.Count == 0) return;

        var filePath = files[0].Path.LocalPath;
        if (!_urlParser.IsSupportedAudioFile(filePath)) return;

        var (title, artist) = _metadata.FetchFromLocalFile(filePath);
        var track = new Track
        {
            Title = title,
            Artist = artist,
            FilePath = filePath,
            Source = TrackSource.Local
        };

        _library.Add(track);
        NullActionLogger.TrackAdded(track.Id.ToString(), filePath, nameof(TrackInputViewModel));
        TrackAdded?.Invoke();
    }

    private Task AddFolderPathAsync(string folderPath)
    {
        var audioExtensions = new[] { ".mp3", ".flac", ".wav", ".ogg", ".m4a", ".aac" };
        var files = System.IO.Directory.EnumerateFiles(folderPath, "*.*", System.IO.SearchOption.AllDirectories)
            .Where(f => audioExtensions.Contains(System.IO.Path.GetExtension(f).ToLowerInvariant()));

        foreach (var file in files)
        {
            var (t, a) = _metadata.FetchFromLocalFile(file);
            var track = new Track
            {
                Title = t,
                Artist = a,
                FilePath = file,
                Source = TrackSource.Local
            };
            _library.Add(track);
            NullActionLogger.TrackAdded(track.Id.ToString(), file, nameof(TrackInputViewModel));
        }

        ClearInputs();
        IsUrlInputVisible = false;
        TrackAdded?.Invoke();
        return Task.CompletedTask;
    }

    private async Task HandleSpotifyUrlAsync(string spotifyUrl)
    {
        StatusMessage = "Looking up Spotify track...";
        var result = await _spotifyBridge.BridgeAsync(spotifyUrl);

        if (!result.Found)
        {
            StatusMessage = "Could not find a YouTube match for this Spotify track";
            return;
        }

        var window = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow : null;
        if (window == null) return;

        var message = $"Found on YouTube:\n\n\"{result.YouTubeTitle}\"\n\n" +
                     $"Spotify track: {result.SpotifyTitle} by {result.SpotifyArtist}\n\nAdd to library?";

        var dialog = new Views.ConfirmDialog("Spotify → YouTube Match", message);
        var confirmed = await dialog.ShowDialog<bool>(window);

        if (!confirmed)
        {
            StatusMessage = "Cancelled";
            return;
        }

        var track = new Track
        {
            Title = result.SpotifyTitle,
            Artist = result.SpotifyArtist,
            Url = result.YouTubeUrl,
            Source = TrackSource.Spotify
        };

        _library.Add(track);
        NullActionLogger.TrackAdded(track.Id.ToString(), result.YouTubeUrl, nameof(TrackInputViewModel));
        TrackAdded?.Invoke();
        StatusMessage = $"Added: {track.Title}";

        _ = Task.Run(async () =>
            await _download.DownloadAsync(
                track.Id.ToString(), result.YouTubeUrl,
                _settings.AudioFormat, _settings.AudioQuality));
    }

    private void ClearInputs()
    {
        InputUrl = string.Empty;
        InputTitle = string.Empty;
        InputArtist = string.Empty;
        SelectedSource = TrackSource.Unknown;
    }
}