using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using NullWave.Helpers;
using NullWave.Helpers.Logging;
using NullWave.Models;
using NullWave.Services;
using NullWave.Services.Integration; 
using NullWave.Services.Plugins;
using NullWave.ViewModels.Base;
using NullWave.Services.Metadata;
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
    private readonly AlbumArtService _albumArtService;
    
    private string _inputUrl = string.Empty;
    private string _lastFetchedUrl = string.Empty;
    private string _inputTitle = string.Empty;
    private string _inputArtist = string.Empty;
    private TrackSource _selectedSource = TrackSource.Unknown;
    private CancellationTokenSource? _fetchDebounce;
    private string? _autoTitle;
    private string? _autoArtist;
    private readonly Dictionary<string, Guid> _pendingIds = new(); // FIX: Track IDs for in-flight fetches
    
    public event Action? RadioCatalogRequested;
    private bool _isFetching;
    private bool _isUrlInputVisible;
    private string _statusMessage = string.Empty;
    private readonly HashSet<string> _fetchesInFlight = new();
    private string? _lastFetchedThumbnail;

    public Array SourceOptions => Enum.GetValues(typeof(TrackSource));
    public ICommand AddTrackCommand { get; }
    public ICommand AddLocalFileCommand { get; }
    public ICommand ShowUrlInputCommand { get; }
    public ICommand BrowseRadioCatalogCommand { get; }
    
    public event Action? TrackAdded;
    public event Action? TrackMetadataUpdated;
    public event Action<string>? PlaylistImportRequested;
    public event Action<string, IReadOnlyList<RadioChannel>>? RadioSiteRequested;

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
            OnPropertyChanged(nameof(IsInputUrlValid));
            
            var normalized = SourceDetector.NormalizeUrl(value);

            if (normalized != _lastFetchedUrl)
            {
                if (InputTitle == _autoTitle)   InputTitle = string.Empty;
                if (InputArtist == _autoArtist) InputArtist = string.Empty;
                _autoTitle = _autoArtist = null;
                _lastFetchedThumbnail = null;
            }

            if (ShareLink.TryParseTrack(normalized, out _, out _))
            {
                SelectedSource = TrackSource.Unknown;
                return;
            }

            SelectedSource = SourceDetector.Detect(normalized);
            if (_urlParser.IsValidUrl(normalized) && SourceDetector.IsPlayableUrl(normalized) && normalized != _lastFetchedUrl
                && DetectMediaType(normalized) != MediaType.Radio)
            {
                _lastFetchedUrl = normalized;
                _fetchDebounce?.Cancel();
                var cts = _fetchDebounce = new CancellationTokenSource();
                _ = Task.Delay(400, cts.Token).ContinueWith(t => 
                { 
                    if (!t.IsCanceled) Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = FetchMetadataAsync(normalized)); 
                }, TaskScheduler.Default);
            }
        }
    }

    public bool IsInputUrlValid
    {
        get
        {
            var url = SourceDetector.NormalizeUrl(InputUrl);
            if (string.IsNullOrWhiteSpace(url)) return false;
            
            if (ShareLink.IsProfileLink(url) || 
                url.StartsWith("NW1.", StringComparison.OrdinalIgnoreCase) || 
                url.StartsWith("NW-", StringComparison.OrdinalIgnoreCase)) return true;

            if (ShareLink.TryParseTrack(url, out _, out _)) return true;
            
            if (_urlParser.IsValidUrl(url) && SourceDetector.IsPlayableUrl(url)) return true;
            if (Directory.Exists(url)) return true;
            if (File.Exists(url) && _urlParser.IsSupportedAudioFile(url)) return true;
            return false;
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
        AlbumArtService albumArtService)
    {
        _library = library;
        _metadata = metadata;
        _urlParser = urlParser;
        _download = download;
        _spotifyBridge = spotifyBridge;
        _settings = settings;
        _albumArtService = albumArtService;

        _download.DownloadCompleted += async (trackId, filePath, _) =>
        {
            if (!Guid.TryParse(trackId, out var trackGuid)) return;
            var track = _library.GetAll().FirstOrDefault(t => t.Id == trackGuid);
            if (track == null) return;
            track.FilePath = filePath;
            track.AlbumArtPath = await _albumArtService.GetArtPathAsync(track);
            _library.Update(track);
        };

        AddTrackCommand = new SafeRelayCommand(AddTrackSafeAsync, errorScope: "add-track");
        AddLocalFileCommand = new RelayCommand(async () => await AddLocalFileAsync());
        ShowUrlInputCommand = new RelayCommand(() => IsUrlInputVisible = !IsUrlInputVisible);
        BrowseRadioCatalogCommand = new RelayCommand(() => RadioCatalogRequested?.Invoke());
    }

    private static string StripQueryStringForDisplay(string url)
    {
        var qIndex = url.IndexOf('?');
        return qIndex > 0 ? url[..qIndex] : url;
    }

    private static bool IsYouTubePlaylist(string url) =>
        SourceDetector.Detect(url) == TrackSource.YouTube &&
        url.Contains("/playlist?", StringComparison.OrdinalIgnoreCase);

    public void AddTrack() => _ = AddTrackSafeAsync();

    private async Task AddTrackSafeAsync()
    {
        try { await AddTrackAsync(); }
        catch (Exception ex)
        {
            NullActionLogger.Error(nameof(TrackInputViewModel), ex, "AddTrack failed");
            ToastService.Instance.Show("Couldn't add that track. See the log for details.", ToastType.Warning);
        }
    }

    private async Task AddTrackAsync()
    {
        var providedTitle = InputTitle.Trim();
        var url = SourceDetector.NormalizeUrl(InputUrl); 
        if (string.IsNullOrWhiteSpace(url))
        {
            IsUrlInputVisible = true;
            return;
        }

        if (ShareLink.IsProfileLink(url) || 
            url.StartsWith("NW1.", StringComparison.OrdinalIgnoreCase) || 
            url.StartsWith("NW-", StringComparison.OrdinalIgnoreCase))
        {
            ToastService.Instance.Show(
                "That's a profile link! Open your Profile window and paste it in the 'Connect With A Code' box to see your taste overlap.",
                type: ToastType.Info,
                durationMs: 6000,
                title: "Profile Sharing");
            ClearInputs();
            IsUrlInputVisible = false;
            return;
        }

        if (ShareLink.TryParseTrack(url, out _, out _))
        {
            ToastService.Instance.Show(
                "P2P track sharing is coming soon! For now, please ask your friend to share the direct YouTube or SoundCloud URL.",
                type: ToastType.Info,
                durationMs: 6000,
                title: "Track Sharing");
            ClearInputs();
            IsUrlInputVisible = false;
            return;
        }

        if (IsYouTubePlaylist(url))
        {
            PlaylistImportRequested?.Invoke(url);
            ClearInputs();
            IsUrlInputVisible = false;
            return;
        }

        if (RadioStationCatalog.TryGetChannels(url, out var stationName, out var channels))
        {
            RadioSiteRequested?.Invoke(stationName, channels);
            ClearInputs();
            IsUrlInputVisible = false;
            return;
        }

        if (SelectedSource == TrackSource.Spotify)
        {
            IsUrlInputVisible = false;
            ClearInputs();
            _ = HandleSpotifyUrlAsync(url);
            return;
        }

        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (Directory.Exists(url))
            {
                _ = AddFolderPathAsync(url);
                return;
            }
            if (File.Exists(url) && _urlParser.IsSupportedAudioFile(url))
            {
                var (t, a, duration) = _metadata.FetchFromLocalFile(url);
                var track = new Track
                {
                    Title = string.IsNullOrWhiteSpace(providedTitle) ? t : providedTitle,
                    Artist = InputArtist.Trim().Length > 0 ? InputArtist.Trim() : a,
                    FilePath = url,
                    Source = TrackSource.Local,
                    Duration = duration,
                    MediaType = _metadata.DetectLocalMediaType(url)
                };
                var (album, trackNum) = _metadata.FetchAlbumAndTrackNumber(url);
                track.Album = album;
                track.TrackNumber = trackNum;
                _library.Add(track);
                NullActionLogger.TrackAdded(track.Id.ToString(), url, nameof(TrackInputViewModel));
                ClearInputs();
                IsUrlInputVisible = false;
                TrackAdded?.Invoke();
                return;
            }
        }

        if (!SourceDetector.IsPlayableUrl(url))
        {
            ToastService.Instance.Show(
                "That URL doesn't look like a playable track. Paste a direct video or track link.",
                type: ToastType.Warning,
                durationMs: 4000,
                title: "Invalid URL"
            );
            Log.Warning("[{Source}] Rejected unplayable URL: {Url}", nameof(TrackInputViewModel), url);
            return;
        }

        var usedFallbackTitle = string.IsNullOrWhiteSpace(providedTitle);
        var fallbackTitle = SelectedSource switch
        {
            TrackSource.SoundCloud => "SoundCloud track",
            TrackSource.YouTube    => "YouTube track",
            TrackSource.Spotify    => "Spotify track",
            TrackSource.Bandcamp   => "Bandcamp track",
            TrackSource.Audiomack  => "Audiomack track",
            TrackSource.ArchiveOrg => "Archive.org track",
            TrackSource.Mixcloud   => "Mixcloud set",
            _                      => DetectMediaType(url) == MediaType.Radio
                ? FriendlyRadioName(url)
                : StripQueryStringForDisplay(url)
        };

        var title = usedFallbackTitle ? fallbackTitle : providedTitle;
        var source = SelectedSource;
        var newTrack = new Track
        {
            Title        = title,
            Artist       = InputArtist.Trim(),
            Url          = url,
            Source       = source,
            MediaType    = DetectMediaType(url),
            AlbumArtPath = _lastFetchedThumbnail
        };
        _lastFetchedThumbnail = null;

        // Canonicalize YouTube URLs to prevent duplicates from tracking params like &pp=
        if (!string.IsNullOrEmpty(newTrack.Url) && (newTrack.Url.Contains("youtube.com") || newTrack.Url.Contains("youtu.be")))
        {
            var match = System.Text.RegularExpressions.Regex.Match(newTrack.Url, @"(?:v=|\/)([0-9A-Za-z_-]{11})");
            if (match.Success) newTrack.Url = $"https://www.youtube.com/watch?v={match.Groups[1].Value}";
        }

        // Strip tracking params from display URLs for new sources to help duplicate detection
        if (source is TrackSource.Bandcamp or TrackSource.Audiomack or TrackSource.ArchiveOrg or TrackSource.Mixcloud)
            newTrack.Url = StripQueryStringForDisplay(newTrack.Url);

        if (_library.IsDuplicate(newTrack))
        {
            ToastService.Instance.Show("Track is already in your library.", ToastType.Info, durationMs: 3000);
            ClearInputs();
            IsUrlInputVisible = false;
            return;
        }

        _library.Add(newTrack);
        NullActionLogger.TrackAdded(newTrack.Id.ToString(), newTrack.Url, nameof(TrackInputViewModel));

        if (usedFallbackTitle)
        {
            // FIX: Pass the track ID so backfill can find it even if the URL was canonicalized
            _ = FetchMetadataAsync(url, newTrack.Id);
        }

        ClearInputs();
        IsUrlInputVisible = false;
        TrackAdded?.Invoke();

        if (SourceDetector.IsYtDlpSource(source))
        {
            if (!await _download.EnsureConsentAsync()) return;

            _ = Task.Run(async () =>
                await _download.DownloadAsync(
                    newTrack.Id.ToString(), newTrack.Url,
                    _settings.AudioFormat, _settings.AudioQuality,
                    title: newTrack.Title, artist: newTrack.Artist)); 
        }
    }

    internal static MediaType DetectMediaType(string url)
    {
        if (SourceDetector.IsYtDlpSource(SourceDetector.Detect(url)))
        {
            var lower = url.ToLowerInvariant();
            if (lower.Contains("audiobook") || lower.Contains("librivox") || lower.Contains("spoken word") ||
                lower.Contains("full book") || lower.Contains("archive.org/details/audiobook"))
                return MediaType.Audiobook;
            return MediaType.Music;
        }

        var value = url.ToLowerInvariant();
        if (value.EndsWith(".m3u8") || value.EndsWith(".m3u") || value.EndsWith(".pls") ||
            value.Contains("icecast") || value.Contains("/stream") ||
            RadioStationCatalog.TryGetChannels(url, out _, out _))
        {
            return MediaType.Radio;
        }
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var host = uri.Host.ToLowerInvariant();
            if (host.Contains("radio") && 
                !host.Contains("radiohead") &&  
                (host.StartsWith("radio") || host.EndsWith("radio") || host.Contains(".radio.")))
            {
                return MediaType.Radio;
            }
        }
        if (value.Contains("audiobook") || value.Contains("librivox") || value.Contains("spoken word") ||
            value.Contains("full book") || value.Contains("archive.org/details/audiobook"))
            return MediaType.Audiobook;
        return MediaType.Music;
    }

    private static string FriendlyRadioName(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var u)
            ? u.Host.Replace("www.", "")
            : url;
    }

    private async Task FetchMetadataAsync(string url, Guid? trackId = null)
    {
        if (!_fetchesInFlight.Add(url))
        {
            // FIX: If a fetch is already running, map the new trackId to the URL 
            // so the running fetch can update the correct database row when it finishes.
            if (trackId is { } pid) _pendingIds[url] = pid;
            Log.Debug("[{Source}] Skipping duplicate in-flight metadata fetch for {Url}",
                nameof(TrackInputViewModel), url);
            return;
        }

        IsFetching = true;
        try
        {
            var (title, artist, thumbnail, duration) = await _metadata.FetchFromUrlAsync(url);
            
            // FIX: Recover the trackId if it was registered while we were waiting
            if (trackId == null && _pendingIds.Remove(url, out var pending)) trackId = pending;
            
            // FIX: Only touch the form if the user is still on this URL
            if (SourceDetector.NormalizeUrl(InputUrl) == url)
            {
                if (!string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(InputTitle))
                { 
                    InputTitle = title; 
                    _autoTitle = title; 
                }
                if (!string.IsNullOrWhiteSpace(artist) && string.IsNullOrWhiteSpace(InputArtist)) 
                { 
                    InputArtist = artist; 
                    _autoArtist = artist; 
                }
                _lastFetchedThumbnail = thumbnail;
            }
            
            Log.Information("[{Source}] Metadata fetched: {Title} by {Artist}",
                nameof(TrackInputViewModel), title, artist);

            // FIX: Look up by track ID first to survive URL canonicalization
            var existing = trackId is { } id
                ? _library.GetAll().FirstOrDefault(t => t.Id == id)
                : _library.GetAll().FirstOrDefault(t => t.Url == url);

            if (existing != null)
            {
                if (!string.IsNullOrWhiteSpace(title))
                {
                    var parsed = NullWave.Helpers.TrackNormalizer.TrySplit(title);
                    var cleanTitle  = title;
                    var cleanArtist = artist;
                    if (parsed != null && !string.IsNullOrWhiteSpace(parsed.Value.Title))
                    {
                        cleanTitle = parsed.Value.Title;
                        if (!string.IsNullOrWhiteSpace(parsed.Value.Artist)) cleanArtist = parsed.Value.Artist;
                    }
                    if (NullWave.Helpers.TrackNormalizer.IsPlaceholderTitle(existing.Title))  existing.Title  = cleanTitle;
                    if (NullWave.Helpers.TrackNormalizer.IsPlaceholderArtist(existing.Artist) &&
                        !string.IsNullOrWhiteSpace(cleanArtist))              existing.Artist = cleanArtist;
                }

                if (string.IsNullOrEmpty(existing.AlbumArtPath) && thumbnail != null)
                    existing.AlbumArtPath = thumbnail;
                if (existing.Duration == TimeSpan.Zero && duration > TimeSpan.Zero)
                    existing.Duration = duration;

                _library.Update(existing);
                _download.UpdateJobMetadata(existing.Id.ToString(), existing.Title, existing.Artist); 
                Avalonia.Threading.Dispatcher.UIThread.Post(
                    () => TrackMetadataUpdated?.Invoke());
                Log.Information("[{Source}] Backfilled track metadata: {Title} by {Artist}",
                    nameof(TrackInputViewModel), existing.Title, existing.Artist);
            }
        }
        catch (Exception ex)
        {
            NullActionLogger.Error(nameof(TrackInputViewModel), ex, $"Metadata fetch failed for {url}");
        }
        finally
        {
            _fetchesInFlight.Remove(url);
            _pendingIds.Remove(url);
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

        var (title, artist, duration) = _metadata.FetchFromLocalFile(filePath);
        var track = new Track
        {
            Title = title,
            Artist = artist,
            FilePath = filePath,
            Source = TrackSource.Local,
            Duration = duration,
            MediaType = _metadata.DetectLocalMediaType(filePath)
        };
        var (album, trackNum) = _metadata.FetchAlbumAndTrackNumber(filePath);
        track.Album = album;
        track.TrackNumber = trackNum;

        _library.Add(track);
        NullActionLogger.TrackAdded(track.Id.ToString(), filePath, nameof(TrackInputViewModel));
        TrackAdded?.Invoke();
    }

    private Task AddFolderPathAsync(string folderPath)
    {
        var audioExtensions = new[] { ".mp3", ".flac", ".wav", ".ogg", ".m4a", ".aac" };
        var files = Directory.EnumerateFiles(folderPath, "*.*", SearchOption.AllDirectories)
            .Where(f => audioExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()));

        foreach (var file in files)
        {
            var (t, a, duration) = _metadata.FetchFromLocalFile(file);
            var track = new Track
            {
                Title = t,
                Artist = a,
                FilePath = file,
                Source = TrackSource.Local,
                Duration = duration,
                MediaType = _metadata.DetectLocalMediaType(file)
            };
            var (album, trackNum) = _metadata.FetchAlbumAndTrackNumber(file);
            track.Album = album;
            track.TrackNumber = trackNum;
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

        var message = $"Found on YouTube:\n\"{result.YouTubeTitle}\"\n" +
                      $"Spotify track: {result.SpotifyTitle} by {result.SpotifyArtist}\nAdd to library?";
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

        if (!await _download.EnsureConsentAsync())
        {
            StatusMessage = "Downloads disabled";
            return;
        }
 
        _ = Task.Run(async () =>
            await _download.DownloadAsync(
                track.Id.ToString(), result.YouTubeUrl,
                _settings.AudioFormat, _settings.AudioQuality,
                title: track.Title, artist: track.Artist)); 
    }

    private void ClearInputs()
    {
        InputUrl = string.Empty;
        InputTitle = string.Empty;
        InputArtist = string.Empty;
        SelectedSource = TrackSource.Unknown;
        _lastFetchedThumbnail = null;
        _lastFetchedUrl = string.Empty;
        _autoTitle = _autoArtist = null;
    }
}