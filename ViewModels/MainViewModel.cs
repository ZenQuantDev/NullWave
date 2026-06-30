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
    private readonly PlaylistService _playlists;
    private readonly LastFmService _lastFm;
    private readonly MetadataService _metadata;
    private readonly UrlParserService _urlParser = new();
    private readonly ExportService _export = new();
    private readonly PlaybackService _playbackService = new();
    private readonly DownloadService _downloadService;
    private readonly SpotifyBridgeService _spotifyBridge;
    private readonly PreferencesService _prefsService;
    private readonly AlbumArtService _albumArt;
    private readonly LastFmEnrichmentService _enrichment;
    private readonly WeatherService _weatherService;
    private readonly LocalAIService _localAI;
    private readonly MoodPlaylistService _moodPlaylist;
    private readonly PowerStateService _powerState;

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

    private string _currentPage = "Library";
    public string CurrentPage
    {
        get => _currentPage;
        set { _currentPage = value; OnPropertyChanged(); }
    }

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
        
        var dbService = new DatabaseService();
        _library = new LibraryService(dbService, _metadata);
        _playlists = new PlaylistService(dbService, _library);
        
        _downloadService = new DownloadService(_library);

        _prefsService = new PreferencesService();

        _albumArt = new AlbumArtService(_lastFm);
        _enrichment = new LastFmEnrichmentService(_lastFm, _library, _albumArt);

        _weatherService = new WeatherService(_keyStore);
        _localAI = new LocalAIService();
        _moodPlaylist = new MoodPlaylistService(_weatherService, _localAI, _library);

        Settings = new SettingsViewModel(_keyStore, _secureDelete, _prefsService);

        var playlistImport = new PlaylistImportViewModel(_library, _metadata, _downloadService);
        Input = new TrackInputViewModel(_library, _metadata, _urlParser, _downloadService, _spotifyBridge, Settings, playlistImport);
        Library = new LibraryViewModel(_library);
        Playlist = new PlaylistViewModel(_playlists);
        Export = new ExportViewModel(_library, _export);
        Detail = new TrackDetailViewModel(_library);
        Import = new ImportViewModel(_library, _metadata);
        Player = new PlayerViewModel(_playbackService, _downloadService, _library, Settings, _metadata);
        Profile = new UserProfileViewModel(_library);
        

        Settings.RefreshWeatherRequested += () => _ = RunMoodPlaylistAsync(forceRefresh: true);

        Settings.AIFeaturesEnabledChanged += enabled =>
        {
            if (!enabled)
            {
                Settings.StopHealthCheck();
                Log.Information("[MainViewModel] AI features disabled - health check stopped");
            }
            else
            {
                _ = Task.Run(async () =>
                {
                    var ai = new NullWave.Services.SmartSorting.LocalAIService();
                    bool running = await ai.PingAsync();
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        Settings.SetAIServiceState(running
                            ? NullWave.ViewModels.AIServiceState.Running
                            : NullWave.ViewModels.AIServiceState.Stopped));
                });
                Settings.StartAIHealthCheck();
                Log.Information("[MainViewModel] AI features re-enabled - health check restarted");
            }
        };

        _powerState = new PowerStateService();

        _powerState.PowerStateChanged += state =>
        {
            _localAI.OnPowerStateChanged(state);

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                Settings.PowerStateLabel = state switch
                {
                    PowerState.AC      => "Plugged in (AC)",
                    PowerState.Battery => "On battery",
                    _                  => "Unknown"
                });
        };

        Settings.PowerStateLabel = PowerStateService.ReadPowerState() switch
        {
            PowerState.AC      => "Plugged in (AC)",
            PowerState.Battery => "On battery",
            _                  => "Unknown"
        };

        Settings.PowerModelsChanged += (batteryModel, perfModel, autoSwitch) =>
            _localAI.ConfigurePowerModels(batteryModel, perfModel, autoSwitch);

        _localAI.ConfigurePowerModels(
            Settings.BatteryModel,
            Settings.PerformanceModel,
            Settings.AutoPowerModelSwitch);

        _powerState.StartPolling();

        Settings.MaxConcurrentDownloadsChanged += limit =>
            _downloadService.UpdateConcurrencyLimit(limit);
        
        _downloadService.UpdateConcurrencyLimit(Settings.MaxConcurrentDownloads);

        _downloadService.DownloadCompleted += (_, _, _) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => Library.Refresh());
        };
        _downloadService.DownloadFailed += (_, _, _) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => Library.Refresh());
        };

        Player.UpdateSkipPenaltyCap(Settings.SkipPenaltyCap);

        Input.TrackMetadataUpdated += Library.Refresh;
        Library.TrackDetailRequested += Detail.OpenFor;
        Library.PlayTrackRequested += Player.PlayTrack;
        Import.ImportCompleted += Library.Refresh;

        Input.TrackAdded += () =>
        {
            var track = _library.GetAll().LastOrDefault();
            if (track == null) return;

            var playlistUrl = track.Url;
            if (string.IsNullOrEmpty(playlistUrl)) return;

            if (playlistUrl.Contains("list="))
            {
                Log.Information("[MainViewModel] Intercepted playlist URL, removing dummy track and starting bulk download");
                _library.Remove(track.Id);
                Library.Refresh();
                
                _ = _downloadService.DownloadPlaylistAsync(
                    playlistUrl: playlistUrl,
                    onTrackReady: (downloadedTrack) =>
                    {
                        _enrichment.EnrichAsync(downloadedTrack);
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => Library.Refresh());
                    });
                    
                return;
            }
            
            _enrichment.EnrichAsync(track);
            Library.Refresh();
        };

        _enrichment.BackfillCompleted += () =>
        {
            if (_initialMoodPlaylistRun) return;
            _initialMoodPlaylistRun = true;
            _ = RunMoodPlaylistAsync(forceRefresh: false);
        };

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

        Settings.RepairPathsRequested += () =>
        {
            try
            {
                var (total, missing, removed) = _library.RepairPaths(removeDeadEntries: true);
                Library.Refresh();
                Settings.ReportRepairPathsComplete(total, missing, removed);
            }
            catch (Exception ex)
            {
                Settings.ReportRepairFailed("Repair Paths", ex.Message);
                NullActionLogger.Error(nameof(MainViewModel), ex, "RepairPaths failed");
            }
        };

        Settings.ReimportAssetsRequested += () =>
        {
            try
            {
                var dir = _prefsService.Current.DownloadDirectory;
                if (string.IsNullOrWhiteSpace(dir))
                    dir = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        ".nullwave", "downloads");

                var relinked = _library.ReimportAssets(dir);
                Library.Refresh();
                Settings.ReportReimportComplete(relinked);
            }
            catch (Exception ex)
            {
                Settings.ReportRepairFailed("Reimport Assets", ex.Message);
                NullActionLogger.Error(nameof(MainViewModel), ex, "ReimportAssets failed");
            }
        };

        Settings.ForceMetaResyncRequested += () =>
        {
            try
            {
                var cleared = _library.ClearTagsForReSync();
                Settings.ReportMetaResyncComplete(cleared);
                _ = Task.Run(async () =>
                {
                    await Task.Delay(800);
                    _enrichment.BackfillAsync();
                });

                Library.Refresh();
            }
            catch (Exception ex)
            {
                Settings.ReportRepairFailed("Force Meta Re-sync", ex.Message);
                NullActionLogger.Error(nameof(MainViewModel), ex, "ForceMetaResync failed");
            }
        };

        Settings.GenerateMoodPlaylistRequested += () => _ = RunMoodPlaylistAsync(forceRefresh: true);

        Settings.ExportUntaggedTracksRequested += async () =>
        {
            var untagged = _library.GetAll()
                .Where(t => t.Tags == null || t.Tags.Count == 0)
                .ToList();

            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                && desktop.MainWindow != null)
            {
                await Settings.ReportExportReadyAsync(untagged, desktop.MainWindow);
            }
        };

        Settings.ImportAiTagsRequested += async () =>
        {
            try
            {
                if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop
                    || desktop.MainWindow == null)
                    return null;

                var files = await desktop.MainWindow.StorageProvider.OpenFilePickerAsync(
                    new Avalonia.Platform.Storage.FilePickerOpenOptions
                    {
                        Title          = "Import AI Tag JSON",
                        AllowMultiple  = false,
                        FileTypeFilter = new[]
                        {
                            new Avalonia.Platform.Storage.FilePickerFileType("JSON / Text")
                                { Patterns = new[] { "*.json", "*.txt", "*.md" } },
                            new Avalonia.Platform.Storage.FilePickerFileType("All files")
                                { Patterns = new[] { "*" } },
                        }
                    });

                if (files.Count == 0)
                {
                    Settings.ReportImportComplete(0, 0);
                    return null;
                }

                string jsonContent;
                await using (var stream = await files[0].OpenReadAsync())
                using (var reader = new System.IO.StreamReader(stream))
                    jsonContent = await reader.ReadToEndAsync();

                var externalAI = new NullWave.Services.SmartSorting.ExternalAITagService();
                var results = externalAI.ParseImportedJson(jsonContent);

                if (results.Count == 0)
                {
                    Settings.ReportImportFailed("No valid tag entries found in the file.");
                    return null;
                }

                int applied = 0;
                foreach (var result in results)
                {
                    var track = _library.GetAll().FirstOrDefault(t => t.Id == result.Id);
                    if (track == null || result.Tags.Count == 0) continue;

                    track.Tags ??= new System.Collections.Generic.List<string>();
                    foreach (var tag in result.Tags)
                    {
                        if (!track.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                            track.Tags.Add(tag);
                    }

                    _library.Update(track);
                    applied++;
                }

                Library.Refresh();
                Settings.ReportImportComplete(applied, results.Count);
                return null;
            }
            catch (Exception ex)
            {
                Settings.ReportImportFailed(ex.Message);
                NullActionLogger.Error(nameof(MainViewModel), ex, "External AI import failed");
                return null;
            }
        };

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

    public void DisposePowerState() => _powerState.Dispose();

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

    private async Task RunMoodPlaylistAsync(bool forceRefresh)
    {
        try
        {
            if (!_weatherService.IsConfigured)
            {
                Log.Information("[MainViewModel] OpenWeather API key not set - skipping mood playlist");
                return;
            }

            var lat = Settings.Latitude;
            var lon = Settings.Longitude;

            if (lat == 0 && lon == 0)
            {
                Settings.ReportMoodPlaylistFailed("No location set - add coordinates in Settings → Smart Sorting");
                return;
            }

            _localAI.CurrentModel = Settings.SelectedModel;
            bool useAi = Settings.AIFeaturesEnabled && Settings.UseLocalAI;

            var result = await _moodPlaylist.GenerateAsync(lat, lon, useAi, forceRefresh);

            if (!result.Success)
            {
                Settings.ReportMoodPlaylistFailed(result.FailureReason ?? "Unknown error");
                return;
            }

            var oldMoodPlaylists = _playlists.GetAll().Where(p => p.Name.StartsWith("Mood:")).ToList();
            foreach (var old in oldMoodPlaylists)
            {
                _playlists.Remove(old.Id);
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