using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Input;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using NullWave.Helpers;
using NullWave.Helpers.Logging;
using NullWave.Services;
using NullWave.Services.Integration;
using NullWave.Services.SmartSorting;
using NullWave.ViewModels.Base;
using Serilog;

namespace NullWave.ViewModels;

public class MainViewModel : ViewModelBase
{
    private readonly KeyStoreService _keyStore = new();
    private readonly SecureDeleteService _secureDelete;
    private readonly ConfigService _config;
    private readonly LibraryService _library;
    private readonly PlaylistService _playlists = new();
    private readonly LastFmService _lastFm;
    private readonly MetadataService _metadata;
    private readonly UrlParserService _urlParser = new();
    private readonly ExportService _export = new();
    private readonly PlaybackService _playbackService = new();
    private readonly DownloadService _downloadService = new();
    private readonly SpotifyBridgeService _spotifyBridge;
    private readonly PreferencesService _prefsService;
    private readonly AlbumArtService _albumArt;
    private readonly LastFmEnrichmentService _enrichment;
    private readonly WeatherService _weatherService;
    private readonly LocalAIService _localAI;
    private readonly MoodPlaylistService _moodPlaylist;

    // Placeholder page ViewModels for Queue and Stats
    private readonly PlaceholderPageViewModel _queuePage = new(
        "🎵", "Queue", "The playback queue is coming soon.\nTracks you add to the queue will appear here.");
    private readonly PlaceholderPageViewModel _statsPage = new(
        "📊", "Stats", "Listening stats are coming soon.\nYour play history and trends will appear here.");

    private bool _isMenuBarVisible;
    public bool IsMenuBarVisible
    {
        get => _isMenuBarVisible;
        set { _isMenuBarVisible = value; OnPropertyChanged(); }
    }
    public void ToggleMenuBar() => IsMenuBarVisible = !IsMenuBarVisible;

    // ── Active Page Tracking for Sidebar ──────────────────────────────────
    private string _currentPage = "Library";
    public string CurrentPage
    {
        get => _currentPage;
        set { _currentPage = value; OnPropertyChanged(); }
    }

    // Guard flag — prevents the mood playlist from firing twice
    private bool _initialMoodPlaylistRun;

    public TrackInputViewModel Input { get; }
    public LibraryViewModel Library { get; }
    public PlaylistViewModel Playlist { get; }
    public ExportViewModel Export { get; }
    public SettingsViewModel Settings { get; }
    public TrackDetailViewModel Detail { get; }
    public ImportViewModel Import { get; }
    public PlayerViewModel Player { get; }
    public UserProfileViewModel Profile { get; }
    
    // Public properties for placeholder pages
    public PlaceholderPageViewModel QueuePage => _queuePage;
    public PlaceholderPageViewModel StatsPage => _statsPage;

    public ICommand ExitCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public ICommand OpenProfileCommand { get; }
    public ICommand AboutCommand { get; }
    public ICommand OpenDataFolderCommand { get; }
    public ICommand OpenLogsCommand { get; }
    public ICommand NavigateLibraryCommand { get; }
    public ICommand NavigatePlaylistsCommand { get; }
    public ICommand NavigateQueueCommand { get; }
    public ICommand NavigateStatsCommand { get; }

    public MainViewModel()
    {
        _secureDelete = new SecureDeleteService(_keyStore);
        _config = new ConfigService(_keyStore);
        _lastFm = new LastFmService(_config);
        _metadata = new MetadataService(_config, _lastFm);
        _spotifyBridge = new SpotifyBridgeService(_config);
        _library = new LibraryService(_metadata);
        _prefsService = new PreferencesService();

        // Album art + enrichment
        _albumArt = new AlbumArtService(_lastFm);
        _enrichment = new LastFmEnrichmentService(_lastFm, _library, _albumArt);

        // Smart Sorting services
        _weatherService = new WeatherService(_keyStore);
        _localAI = new LocalAIService();
        _moodPlaylist = new MoodPlaylistService(_weatherService, _localAI, _library);

        // Construct Settings first
        Settings = new SettingsViewModel(_keyStore, _secureDelete, _prefsService);

        var playlistImport = new PlaylistImportViewModel(_library, _metadata, _downloadService);

        // Construct other ViewModels
        Input = new TrackInputViewModel(_library, _metadata, _urlParser, _downloadService, _spotifyBridge, Settings, playlistImport);
        Library = new LibraryViewModel(_library);
        Playlist = new PlaylistViewModel(_playlists);
        Export = new ExportViewModel(_library, _export);
        Detail = new TrackDetailViewModel(_library);
        Import = new ImportViewModel(_library, _metadata);
        Player = new PlayerViewModel(_playbackService, _downloadService, _library, Settings, _metadata);
        Profile = new UserProfileViewModel(_library);

        // ── Wire events ─────────────────────────────────────────────────
        Input.TrackAdded += Library.Refresh;
        Input.TrackMetadataUpdated += Library.Refresh;
        Library.TrackDetailRequested += Detail.OpenFor;
        Library.PlayTrackRequested += Player.PlayTrack;
        Import.ImportCompleted += Library.Refresh;

        // Last.fm enrichment on track add
        Input.TrackAdded += () =>
        {
            var latest = _library.GetAll().LastOrDefault();
            if (latest != null) _enrichment.EnrichAsync(latest);
        };

        // Backfill completion → first mood playlist
        _enrichment.BackfillCompleted += () =>
        {
            if (_initialMoodPlaylistRun) return;
            _initialMoodPlaylistRun = true;
            _ = RunMoodPlaylistAsync(forceRefresh: false);
        };

        // Backfill existing untagged tracks 3s after startup
        _ = Task.Run(async () =>
        {
            await Task.Delay(3000);
            _enrichment.BackfillAsync();
        });

        Player.PlaySelectedTrackRequested += () =>
        {
            if (Library.SelectedTrack != null)
                Player.PlayTrack(Library.SelectedTrack);
            else if (Library.Tracks.Count > 0)
                Player.PlayTrack(Library.Tracks[0]);
        };

        Player.TrackScrobbleRequested += async (title, artist, playedAt) =>
        {
            if (Settings.ScrobbleToLastFm)
                await _lastFm.ScrobbleAsync(title, artist, playedAt);
        };

        // Thumbnail clearing
        Settings.ClearThumbnailsRequested += () =>
        {
            var cleared = _library.GetAll().Count(t => !string.IsNullOrEmpty(t.AlbumArtPath));
            _library.ClearAllArt();
            Settings.ReportThumbnailsCleared(cleared);
            _library.RebackfillThumbnails();
            _ = Task.Run(async () =>
            {
                await Task.Delay(500);
                _enrichment.BackfillAsync();
            });
            Library.Refresh();
        };

        // Manual mood playlist regenerate
        Settings.GenerateMoodPlaylistRequested += () => _ = RunMoodPlaylistAsync(forceRefresh: true);

        // ── Commands ────────────────────────────────────────────────────
        ExitCommand = new RelayCommand(() =>
        {
            NullActionLogger.User("AppExit", "shutdown", nameof(MainViewModel));
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.Shutdown();
        });

        OpenSettingsCommand = new RelayCommand(() =>
        {
            NullActionLogger.SettingChanged("SettingsOpened", nameof(MainViewModel));
            OpenSettings();
        });

        OpenProfileCommand = new RelayCommand(() =>
        {
            NullActionLogger.User("ProfileOpened", "profile", nameof(MainViewModel));
            OpenProfileWindow();
        });

        AboutCommand = new RelayCommand(() =>
            Log.Information("[{Source}] About dialog requested", nameof(MainViewModel)));

        OpenDataFolderCommand = new RelayCommand(() =>
        {
            var dir = NullWavePaths.DataDir;
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
            Log.Information("[{Source}] Opened data folder: {Dir}", nameof(MainViewModel), dir);
        });

        OpenLogsCommand = new RelayCommand(() =>
        {
            var dir = NullWavePaths.LogsDir;
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
            Log.Information("[{Source}] Opened logs folder: {Dir}", nameof(MainViewModel), dir);
        });

        // Navigation commands - only set CurrentPage
        NavigateLibraryCommand = new RelayCommand(() =>
        {
            CurrentPage = "Library";
            LogNav("Library");
        });
        
        NavigatePlaylistsCommand = new RelayCommand(() =>
        {
            CurrentPage = "Playlists";
            LogNav("Playlists");
        });
        
        NavigateQueueCommand = new RelayCommand(() =>
        {
            CurrentPage = "Queue";
            LogNav("Queue");
        });
        
        NavigateStatsCommand = new RelayCommand(() =>
        {
            CurrentPage = "Stats";
            LogNav("Stats");
        });

        _ = RunStartupDiagnosticsAsync();
    }

    private async System.Threading.Tasks.Task RunStartupDiagnosticsAsync()
    {
        try
        {
            var diag = new StartupDiagnosticsService(_keyStore, _library);
            await diag.RunAsync();
        }
        catch (Exception ex)
        {
            NullActionLogger.Error(nameof(MainViewModel), ex, "Startup diagnostics failed");
        }
    }

    private void OpenSettings()
    {
        var win = new Views.SettingsWindow { DataContext = Settings };
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow != null)
            win.ShowDialog(desktop.MainWindow);
        else
            win.Show();
    }

    private void OpenProfileWindow()
    {
        var win = new Views.ProfileWindow { DataContext = Profile };
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow != null)
            win.ShowDialog(desktop.MainWindow);
        else
            win.Show();
    }

    // ── Mood Playlist Generation ────────────────────────────────────────
    private async Task RunMoodPlaylistAsync(bool forceRefresh)
    {
        try
        {
            if (!_weatherService.IsConfigured)
            {
                Log.Information("[MainViewModel] OpenWeather API key not set — skipping mood playlist");
                return;
            }

            var lat = Settings.Latitude;
            var lon = Settings.Longitude;

            if (lat == 0 && lon == 0)
            {
                Settings.ReportMoodPlaylistFailed("No location set — add coordinates in Settings → Smart Sorting");
                return;
            }

            _localAI.CurrentModel = Settings.SelectedModel;

            var result = await _moodPlaylist.GenerateAsync(
                lat, lon, Settings.UseLocalAI, forceRefresh);

            if (!result.Success)
            {
                Settings.ReportMoodPlaylistFailed(result.FailureReason ?? "Unknown error");
                return;
            }

            var playlistName = $"Mood: {result.Mood} ({result.WeatherCondition}, {result.TemperatureC:F0}°C)";
            var playlist = _playlists.Create(playlistName,
                $"Auto-generated {(result.UsedAI ? "by local AI" : "from tags")} on {DateTime.Now:dd MMM, HH:mm}");

            foreach (var track in result.Tracks)
                _playlists.AddTrack(playlist.Id, track);

            Playlist.Refresh();
            Settings.ReportMoodPlaylistGenerated(result.Tracks.Count, result.Mood);

            Log.Information("[MainViewModel] Mood playlist generated: {Name} ({Count} tracks, AI={UsedAI})",
                playlistName, result.Tracks.Count, result.UsedAI);
        }
        catch (Exception ex)
        {
            Settings.ReportMoodPlaylistFailed(ex.Message);
            NullActionLogger.Error(nameof(MainViewModel), ex, "Mood playlist generation failed");
        }
    }

    private static void LogNav(string destination)
        => NullActionLogger.User("Navigate", destination, nameof(MainViewModel));
}