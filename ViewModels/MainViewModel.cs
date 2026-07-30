using System;
using System.Collections.ObjectModel;
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
using NullWave.Services.Plugins;
using NullWave.Services.SmartSorting;
using NullWave.ViewModels.Base;
using NullWave.Models;
using Serilog;

namespace NullWave.ViewModels;

public class MainViewModel : ViewModelBase
{
    private readonly KeyStoreService _keyStore = new();
    private readonly SecureDeleteService _secureDelete;
    private readonly ConfigService _config;
    private readonly LibraryService _library;
    private readonly PlaylistService _playlists;
    private LastFmService _lastFm;
    private readonly MetadataService _metadata;
    private readonly UrlParserService _urlParser = new();
    private readonly ExportService _export = new();
    private readonly PlaybackService _playbackService = new();
    private readonly DownloadService _downloadService;
    private readonly SpotifyBridgeService _spotifyBridge;
    private readonly PreferencesService _prefsService;
    private readonly LastFmEnrichmentService _enrichment;
    private readonly WeatherService _weatherService;
    private readonly LocalAIService _localAI;
    private readonly MoodPlaylistService _moodPlaylist;
    private readonly PowerStateService _powerState;
    private readonly PluginManager _plugins = new();

    private string? _pendingLastFmToken;
    private LastFmAuthService? _pendingLastFmAuth;

    private LiveNotification? _currentPlaylistActivity;

    private bool _isMenuBarVisible;
    public bool IsMenuBarVisible
    {
        get => _isMenuBarVisible;
        set { _isMenuBarVisible = value; OnPropertyChanged(); }
    }
    public void ToggleMenuBar() => IsMenuBarVisible = !IsMenuBarVisible;

    private bool _isCustomizingSidebar;
    public bool IsCustomizingSidebar
    {
        get => _isCustomizingSidebar;
        set { _isCustomizingSidebar = value; OnPropertyChanged(); }
    }
    public ICommand ToggleCustomizeSidebarCommand { get; }

    private bool _isSidebarCollapsed;
    public bool IsSidebarCollapsed
    {
        get => _isSidebarCollapsed;
        set
        {
            _isSidebarCollapsed = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SidebarCollapseIconKind));
        }
    }
    public Material.Icons.MaterialIconKind SidebarCollapseIconKind =>
        IsSidebarCollapsed ? Material.Icons.MaterialIconKind.ChevronRight : Material.Icons.MaterialIconKind.ChevronLeft;
    public ICommand ToggleSidebarCollapsedCommand { get; }

    private string _currentPage = "Library";
    public string CurrentPage
    {
        get => _currentPage;
        set
        {
            _currentPage = value;
            OnPropertyChanged();
            Nav?.SetActivePage(value);
        }
    }

    private bool _initialMoodPlaylistRun;
    private bool _isMaintenanceRunning;

    public TrackInputViewModel Input { get; }
    public LibraryViewModel Library { get; }
    public PlaylistViewModel Playlist { get; }
    public ExportViewModel Export { get; }
    public SettingsViewModel Settings { get; }
    public TrackDetailViewModel Detail { get; }
    public ImportViewModel Import { get; }
    public PlayerViewModel Player { get; }
    public UserProfileViewModel Profile { get; }

    public ObservableCollection<LiveNotification> ActiveToasts => ToastService.Instance.ActiveToasts;

    public ICommand ExitCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public ICommand OpenProfileCommand { get; }
    public ICommand AboutCommand { get; }
    public ICommand OpenDataFolderCommand { get; }
    public ICommand OpenLogsCommand { get; }
    public ICommand NavigateLibraryCommand { get; }
    public ICommand NavigatePlaylistsCommand { get; }
    public ICommand NavigateToPlaylistCommand { get; }
    public ICommand ToggleQueueCommand { get; }

    public NavigationViewModel Nav { get; private set; } = null!;
    public QueueViewModel Queue { get; private set; } = null!;

    public MainViewModel()
    {
        _prefsService = new PreferencesService();

        _secureDelete = new SecureDeleteService(_keyStore);
        _config = new ConfigService(_keyStore);
        _lastFm = new LastFmService(_config);
        _metadata = new MetadataService(_config, _lastFm);
        _spotifyBridge = new SpotifyBridgeService(_config);

        var dbService = new DatabaseService();
        _library = new LibraryService(dbService, _metadata);
        _playlists = new PlaylistService(dbService, _library);
        var albumArtService = new AlbumArtService(_lastFm);
        _downloadService = new DownloadService(_library, _prefsService, albumArtService);
        _localAI = new LocalAIService();
        _enrichment = new LastFmEnrichmentService(_lastFm, _library, _localAI, _prefsService);
        _weatherService = new WeatherService(_keyStore);
        _moodPlaylist = new MoodPlaylistService(_weatherService, _localAI, _library);

        // =========================================================================
        // PHASE 13: Register all plugin providers
        // =========================================================================
        _plugins.Register(new YtDlpDownloadProvider(_downloadService, _prefsService));
        _plugins.Register(new LastFmMetadataProvider(_lastFm, _prefsService));
        _plugins.Register(new OpenWeatherProvider(_weatherService, _prefsService));
        _plugins.Register(new OllamaAIProvider(_localAI, _prefsService));

        Settings = new SettingsViewModel(_keyStore, _secureDelete, _prefsService, _localAI, _plugins);
        Settings.ClearYtDlpCacheRequested += OnClearYtDlpCacheRequested;

        Input = new TrackInputViewModel(_library, _metadata, _urlParser, _downloadService, _spotifyBridge, Settings, albumArtService);
        Library = new LibraryViewModel(_library);
        Playlist = new PlaylistViewModel(_playlists);
        Export = new ExportViewModel(_library, _export);
        Detail = new TrackDetailViewModel(_library, _plugins);
        Import = new ImportViewModel(_library, _metadata);
        Player = new PlayerViewModel(_playbackService, _downloadService, _library, Settings, _metadata);
        Profile = new UserProfileViewModel(_library);
        Queue = new QueueViewModel(_library);
        Queue.PlayTrackRequested += Player.PlayTrack;

        // Wire pin/unpin events
        Playlist.PinRequested += p => Nav.PinPlaylist(p.Id, p.Name);
        Playlist.UnpinRequested += p => Nav.UnpinPlaylist(p.Id);

        // Wire new events
        Library.NavigateToLibraryRequested += () => CurrentPage = "Library";
        Playlist.PlaylistsChanged += () => Nav.RefreshPlaylistLists();

        Settings.RefreshWeatherRequested += () => _ = RunMoodPlaylistAsync(forceRefresh: true);

        _localAI.FallbackNotice += message =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                ToastService.Instance.Show(message, ToastType.Warning));
        };

        Settings.SweepOrphanedFilesRequested += dryRun =>
        {
            if (_isMaintenanceRunning) return;
            _isMaintenanceRunning = true;

            _ = Task.Run(() =>
            {
                var dir = _prefsService.Current.DownloadDirectory;
                if (string.IsNullOrWhiteSpace(dir))
                {
                    dir = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        ".nullwave",
                        "downloads"
                    );
                }

                var (scanned, orphaned, deleted, failed) = _library.SweepOrphanedFiles(dir, dryRun);

                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    Settings.ReportSweepComplete(scanned, orphaned, deleted, failed, dryRun);
                    _isMaintenanceRunning = false;
                });
            });
        };

        Settings.VacuumDatabaseRequested += () =>
        {
            if (_isMaintenanceRunning) return;
            _isMaintenanceRunning = true;

            _ = Task.Run(() =>
            {
                var (before, after) = _library.VacuumDatabase();
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    Settings.ReportVacuumComplete(before, after);
                    _isMaintenanceRunning = false;
                });
            });
        };

        Settings.VerifyLinksRequested += () =>
        {
            if (_isMaintenanceRunning) return;
            _isMaintenanceRunning = true;

            _ = Task.Run(() =>
            {
                var (checkedCount, mismatches) = _library.VerifyLinks();

                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    Settings.ReportVerifyLinksComplete(checkedCount, mismatches.Count);
                    _isMaintenanceRunning = false;
                });
            });
        };

        Settings.RemoveDuplicatesRequested += dryRun =>
        {
            if (_isMaintenanceRunning) return;
            _isMaintenanceRunning = true;

            _ = Task.Run(() =>
            {
                var (scanned, groups, removed) = _library.RemoveDuplicates(dryRun);
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    Settings.ReportDedupeComplete(scanned, groups, removed, dryRun);
                    Library.Refresh();
                    Library.RefreshArtistGroups();
                    _isMaintenanceRunning = false;
                });
            });
        };

        Settings.ForceCleanTitlesRequested += () =>
        {
            if (_isMaintenanceRunning) return;
            _isMaintenanceRunning = true;

            _ = Task.Run(() =>
            {
                var cleaned = _library.ForceCleanTitles();

                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    Settings.ReportForceCleanComplete(cleaned);
                    Library.Refresh();
                    Library.RefreshArtistGroups();
                    _isMaintenanceRunning = false;
                });
            });
        };

        Settings.MergeSimilarArtistsRequested += () =>
        {
            if (_isMaintenanceRunning) return;
            _isMaintenanceRunning = true;

            _ = Task.Run(() =>
            {
                var groups = _library.FindSimilarArtistGroups();
                int merged = 0;
                foreach (var group in groups)
                    merged += _library.MergeArtistGroup(group);

                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    Settings.ReportArtistMergeComplete(groups.Count, merged);
                    Library.Refresh();
                    Library.RefreshArtistGroups();
                    _isMaintenanceRunning = false;
                });
            });
        };

        Settings.AIFeaturesEnabledChanged += enabled =>
        {
            if (!enabled)
            {
                Settings.StopHealthCheck();
                Log.Information("[MainViewModel] AI features disabled - health check stopped");
                ToastService.Instance.Show("Local AI models offline.", ToastType.Info);
            }
            else
            {
                var activity = ToastService.Instance.StartLiveActivity(
                    "Local AI Subsystem",
                    "Pinging background inference node server...",
                    isIndeterminate: true
                );

                _ = Task.Run(async () =>
                {
                    bool running = await _localAI.PingAsync();
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        ToastService.Instance.Dismiss(activity);
                        if (running)
                        {
                            Settings.SetAIServiceState(NullWave.ViewModels.AIServiceState.Running);
                            ToastService.Instance.Show("Local LLM engine connected successfully!", ToastType.Success);
                        }
                        else
                        {
                            Settings.SetAIServiceState(NullWave.ViewModels.AIServiceState.Stopped);
                            ToastService.Instance.Show("Could not reach local AI server. Check configurations.", ToastType.Warning);
                        }
                        
                        Settings.StartAIHealthCheck();
                    });
                });
                
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

        _downloadService.DownloadCompleted += (_, _, isInteractive) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                Library.Refresh();
                Library.RefreshArtistGroups();
                if (isInteractive)
                    ToastService.Instance.Show("Track download completed successfully.", ToastType.Success);
            });
        };

        _downloadService.DownloadFailed += (_, _, isInteractive) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                Library.Refresh();
                if (isInteractive)
                    ToastService.Instance.Show("A track download failed. Check your connection or logs.", ToastType.Error);
            });
        };

        _downloadService.PlaylistBatchStarted += totalTracks =>
        {
            _currentPlaylistActivity = ToastService.Instance.StartLiveActivity(
                "Downloading Playlist",
                totalTracks > 0
                    ? $"Downloading Playlist: 0/{totalTracks} (Skipped: 0)..."
                    : "Fetching playlist metadata...",
                isIndeterminate: totalTracks == 0
            );
        };

        _downloadService.PlaylistBatchProgress += (completed, total, skipped) =>
        {
            var doneSoFar = completed + skipped;
            ToastService.Instance.UpdateLiveActivity(
                _currentPlaylistActivity,
                message: $"Downloading Playlist: {completed}/{total} (Skipped: {skipped})...",
                progressValue: total > 0 ? doneSoFar * 100.0 / total : 0,
                isIndeterminate: false
            );
        };

        _downloadService.PlaylistBatchCompleted += (completed, failed, skipped) =>
        {
            var summary = $"Bulk download complete: {completed} downloaded, {failed} unavailable, {skipped} duplicates skipped.";
            ToastService.Instance.CompleteLiveActivity(_currentPlaylistActivity, summary);
            _currentPlaylistActivity = null;
            Avalonia.Threading.Dispatcher.UIThread.Post(() => Library.Refresh());
            Log.Information("[MainViewModel] {Summary}", summary);
        };

        Player.UpdateSkipPenaltyCap(Settings.SkipPenaltyCap);
        Input.TrackMetadataUpdated += Library.Refresh;
        Library.TrackDetailRequested += track =>
        {
            Queue.IsOpen = false;
            Detail.OpenFor(track);
        };
        Library.PlayTrackRequested += Player.PlayTrack;
        Import.ImportCompleted += () => { Library.Refresh(); Library.RefreshArtistGroups(); };

        Input.TrackAdded += () =>
        {
            var track = _library.GetAll().LastOrDefault();
            if (track == null) return;

            var playlistUrl = track.Url;
            if (string.IsNullOrEmpty(playlistUrl)) return;

            if (playlistUrl.Contains("list="))
            {
                if (_plugins.Get<YtDlpDownloadProvider>() is not { } ytDlpProvider || !ytDlpProvider.SupportsUrl(playlistUrl))
                {
                    Log.Information("[MainViewModel] Playlist download skipped — yt-dlp plugin unavailable/disabled");
                    ToastService.Instance.Show("Downloads are disabled. Enable yt-dlp in Settings to import playlists.", ToastType.Warning);
                    _library.Remove(track.Id);
                    Library.Refresh();
                    return;
                }

                Log.Information("[MainViewModel] Intercepted playlist URL, removing dummy track and starting bulk download");
                _library.Remove(track.Id);
                Library.Refresh();
                _ = _downloadService.DownloadPlaylistAsync(
                    playlistUrl: playlistUrl,
                    onTrackReady: (downloadedTrack) =>
                    {
                        if (_plugins.Get<LastFmMetadataProvider>() is { })
                            _enrichment.EnrichAsync(downloadedTrack);
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => Library.Refresh());
                    });
                return;
            }

            // Gated enrichment: only run if Last.fm plugin is available
            if (_plugins.Get<LastFmMetadataProvider>() is { })
            {
                _enrichment.EnrichAsync(track);
            }
            Avalonia.Threading.Dispatcher.UIThread.Post(() => Library.Refresh());
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
            if (PowerStateService.ReadPowerState() != PowerState.Battery)
            {
                _enrichment.BackfillAsync();
            }
            else
            {
                Log.Information("[MainViewModel] Skipping automatic AI backfill to preserve battery life.");
                _initialMoodPlaylistRun = true;
                _ = RunMoodPlaylistAsync(forceRefresh: false);
            }
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
            if (!Settings.ScrobbleToLastFm) return;
            
            // Gated scrobbling: check plugin availability and configuration
            if (_plugins.Get<LastFmMetadataProvider>() is not { } lastFmProvider || !lastFmProvider.IsConfiguredForScrobbling)
            {
                Log.Debug("[MainViewModel] Scrobble requested but Last.fm plugin not configured/available");
                return;
            }

            var success = await lastFmProvider.ScrobbleAsync(title, artist, playedAt);
            if (!success)
            {
                Log.Warning("[MainViewModel] Scrobble failed for '{Title}' by '{Artist}'", title, artist);
                ToastService.Instance.Show($"Scrobble failed for '{title}' by {artist}.", ToastType.Warning);
            }
        };

        Settings.LastFmConnectRequested += async () =>
        {
            try
            {
                var auth = new LastFmAuthService(_config.GetLastFmApiKey(), _config.GetLastFmApiSecret());
                if (!auth.IsConfigured)
                {
                    Settings.ReportLastFmAuthFailed("Set your Last.fm API key and shared secret first.");
                    return;
                }
                var token = await auth.GetRequestTokenAsync();
                if (string.IsNullOrEmpty(token))
                {
                    Settings.ReportLastFmAuthFailed("Could not get a request token from Last.fm.");
                    return;
                }
                var authUrl = auth.GetAuthUrl(token);
                Process.Start(new ProcessStartInfo { FileName = authUrl, UseShellExecute = true });
                Settings.ReportLastFmAwaitingAuth();
                _pendingLastFmToken = token;
                _pendingLastFmAuth  = auth;
            }
            catch (Exception ex)
            {
                Settings.ReportLastFmAuthFailed(ex.Message);
                NullActionLogger.Error(nameof(MainViewModel), ex, "Last.fm connect failed");
            }
        };

        Settings.LastFmConfirmAuthRequested += async () =>
        {
            if (_pendingLastFmAuth == null || string.IsNullOrEmpty(_pendingLastFmToken))
            {
                Settings.ReportLastFmAuthFailed("No pending authorization — click Connect first.");
                return;
            }
            var result = await _pendingLastFmAuth.GetSessionKeyAsync(_pendingLastFmToken);
            if (!result.Success)
            {
                Settings.ReportLastFmAuthFailed(
                    result.Error ?? "Authorization not yet granted — approve access in your browser first.");
                return;
            }
            _keyStore.SaveKey("LastFm:SessionKey", result.SessionKey);
            _keyStore.SaveKey("LastFm:Username", result.Username);
            _pendingLastFmToken = null;
            _pendingLastFmAuth  = null;
            _lastFm = new LastFmService(_config);
            Settings.ReportLastFmConnected(result.Username);
            ToastService.Instance.Show($"Successfully connected to Last.fm as {result.Username}!", ToastType.Success);
        };

        Settings.LastFmDisconnectRequested += () =>
        {
            _keyStore.DeleteKey("LastFm:SessionKey");
            _keyStore.DeleteKey("LastFm:Username");
            _lastFm = new LastFmService(_config);
            Settings.ReportLastFmDisconnected();
            ToastService.Instance.Show("Disconnected from Last.fm accounts.", ToastType.Info);
        };

        Settings.ClearThumbnailsRequested += () =>
        {
            if (_isMaintenanceRunning) return;
            _isMaintenanceRunning = true;

            _ = Task.Run(async () =>
            {
                var activity = ToastService.Instance.StartLiveActivity(
                    "Clearing Artwork",
                    "Purging cached thumbnails and resetting index registers...",
                    isIndeterminate: true
                );

                try
                {
                    int cleared = await Task.Run(() =>
                    {
                        var count = _library.GetAll().Count(t => !string.IsNullOrEmpty(t.AlbumArtPath));
                        _library.ClearAllArt();
                        return count;
                    });

                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        Settings.ReportThumbnailsCleared(cleared);
                        _library.RebackfillThumbnails();
                        activity.Title = "Re-indexing Artwork";
                        activity.Message = $"Regenerating asset nodes for {cleared} tracks...";
                    });

                    await Task.Delay(500);
                    _enrichment.BackfillAsync();
                    await Task.Delay(1500);

                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        ToastService.Instance.Dismiss(activity);
                        Library.Refresh();
                        _isMaintenanceRunning = false;
                    });
                }
                catch (Exception ex)
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        ToastService.Instance.Dismiss(activity);
                        ToastService.Instance.Show($"Artwork collection reset failed: {ex.Message}", ToastType.Error);
                        _isMaintenanceRunning = false;
                    });
                }
            });
        };

        Settings.RepairPathsRequested += () =>
        {
            if (_isMaintenanceRunning) return;
            _isMaintenanceRunning = true;

            _ = Task.Run(async () =>
            {
                var activity = ToastService.Instance.StartLiveActivity(
                    "Repairing Paths",
                    "Scanning local track library indices for broken file links...",
                    isIndeterminate: true
                );

                try
                {
                    var (total, missing, removed) = await Task.Run(() => _library.RepairPaths(removeDeadEntries: true));

                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        ToastService.Instance.Dismiss(activity);
                        Library.Refresh();
                        Settings.ReportRepairPathsComplete(total, missing, removed);
                        _isMaintenanceRunning = false;
                    });
                }
                catch (Exception ex)
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        ToastService.Instance.Dismiss(activity);
                        Settings.ReportRepairFailed("Repair Paths", ex.Message);
                        NullActionLogger.Error(nameof(MainViewModel), ex, "RepairPaths failed");
                        _isMaintenanceRunning = false;
                    });
                }
            });
        };

        Settings.ReimportAssetsRequested += () =>
        {
            if (_isMaintenanceRunning) return;
            _isMaintenanceRunning = true;

            _ = Task.Run(async () =>
            {
                var activity = ToastService.Instance.StartLiveActivity(
                    "Validating Assets",
                    "Scanning repository directories to map unlinked tracks...",
                    isIndeterminate: true
                );

                try
                {
                    var dir = _prefsService.Current.DownloadDirectory;
                    if (string.IsNullOrWhiteSpace(dir))
                    {
                        dir = System.IO.Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                            ".nullwave",
                            "downloads"
                        );
                    }

                    var relinked = await Task.Run(() => _library.ReimportAssets(dir));

                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        ToastService.Instance.Dismiss(activity);
                        Library.Refresh();
                        Settings.ReportReimportComplete(relinked);
                        _isMaintenanceRunning = false;
                    });
                }
                catch (Exception ex)
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        ToastService.Instance.Dismiss(activity);
                        Settings.ReportRepairFailed("Reimport Assets", ex.Message);
                        NullActionLogger.Error(nameof(MainViewModel), ex, "ReimportAssets failed");
                        _isMaintenanceRunning = false;
                    });
                }
            });
        };

        Settings.ForceMetaResyncRequested += () =>
        {
            if (_isMaintenanceRunning) return;
            _isMaintenanceRunning = true;

            _ = Task.Run(async () =>
            {
                var activity = ToastService.Instance.StartLiveActivity(
                    "Syncing Metadata",
                    "Flushing local tag database cache registers...",
                    isIndeterminate: true
                );

                try
                {
                    int cleared = await Task.Run(() => _library.ClearTagsForReSync());

                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        Settings.ReportMetaResyncComplete(cleared);
                        Library.Refresh();
                    });

                    await Task.Delay(800);
                    _enrichment.BackfillAsync();
                    await Task.Delay(1200);

                    Avalonia.Threading.Dispatcher.UIThread.Post(() => 
                    {
                        ToastService.Instance.Dismiss(activity);
                        _isMaintenanceRunning = false;
                    });
                }
                catch (Exception ex)
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        ToastService.Instance.Dismiss(activity);
                        Settings.ReportRepairFailed("Force Meta Re-sync", ex.Message);
                        NullActionLogger.Error(nameof(MainViewModel), ex, "ForceMetaResync failed");
                        _isMaintenanceRunning = false;
                    });
                }
            });
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
                    ToastService.Instance.Show("AI Import failed: JSON contained no valid track metadata.", ToastType.Warning);
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
                ToastService.Instance.Show($"Successfully applied AI tags to {applied} tracks!", ToastType.Success);
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
            _ = _plugins.ShutdownAllAsync();
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
            Nav.SetPlaylistActive(null); // Clear pin highlight when navigating generically
            LogNav("Playlists");
        });

        NavigateToPlaylistCommand = new RelayCommand<Playlist>(p =>
        {
            if (p == null) return;
            CurrentPage = "Playlists";
            Playlist.SelectById(p.Id);
            Nav.SetPlaylistActive(p.Id);
            LogNav("Playlists");
        });

        ToggleQueueCommand = new RelayCommand(() =>
        {
            Detail.IsOpen = false;
            Queue.IsOpen = !Queue.IsOpen;
            LogNav("Queue");
        });

        // Nav construction moved here so _playlists is available
        Nav = new NavigationViewModel(
            _prefsService, _playlists,
            NavigateLibraryCommand, NavigatePlaylistsCommand,
            navigateToPlaylist: playlistId =>
            {
                CurrentPage = "Playlists";
                Playlist.SelectById(playlistId);
                Nav.SetPlaylistActive(playlistId);
                LogNav($"PinnedPlaylist:{playlistId}");
            });
        Nav.SetActivePage(CurrentPage);

        Library.RefreshArtistGroups();
        ToggleCustomizeSidebarCommand = new RelayCommand(() => IsCustomizingSidebar = !IsCustomizingSidebar);
        ToggleSidebarCollapsedCommand = new RelayCommand(() => IsSidebarCollapsed = !IsSidebarCollapsed);

        _ = RunStartupDiagnosticsAsync();
    }

    public void DisposePowerState() => _powerState.Dispose();

    private async System.Threading.Tasks.Task RunStartupDiagnosticsAsync()
    {
        try
        {
            var diag = new StartupDiagnosticsService(_keyStore, _library);
            await diag.RunAsync();

            await _plugins.InitializeAllAsync();
        }
        catch (Exception ex)
        {
            NullActionLogger.Error(nameof(MainViewModel), ex, "Startup diagnostics failed");
        }
    }

    private void OnClearYtDlpCacheRequested()
    {
        try
        {
            var processInfo = new ProcessStartInfo("yt-dlp", "--rm-cache-dir")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(processInfo);
            if (process != null)
            {
                process.WaitForExit();
                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();

                if (process.ExitCode == 0)
                {
                    ToastService.Instance.Show("yt-dlp cache cleared successfully!", ToastType.Success, 5000);
                    Log.Information("[MainViewModel] yt-dlp cache cleared successfully");
                }
                else
                {
                    ToastService.Instance.Show($"Failed to clear cache: {error}", ToastType.Error, 5000);
                    Log.Warning("[MainViewModel] yt-dlp cache clear failed: {Error}", error);
                }
            }
        }
        catch (Exception ex)
        {
            ToastService.Instance.Show($"Failed to clear cache: {ex.Message}", ToastType.Error, 5000);
            Log.Error(ex, "[MainViewModel] yt-dlp cache clear failed");
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
            if (_plugins.Get<IWeatherProvider>() is not { } weatherProvider)
            {
                Log.Information("[MainViewModel] OpenWeather plugin unavailable/disabled - skipping mood playlist");
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
                ToastService.Instance.Show($"Could not build mood mix: {result.FailureReason}", ToastType.Error);
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
            ToastService.Instance.Show($"Generated context playlist 'Mood: {result.Mood}' ({result.Tracks.Count} tracks).", ToastType.Success);
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