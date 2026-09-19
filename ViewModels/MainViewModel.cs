using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Input;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using NullWave.Helpers;
using NullWave.Helpers.Logging;
using NullWave.Services;
using NullWave.Services.Integration;
using NullWave.Services.Plugins;
using NullWave.Services.Security;
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
    private readonly UiHangDetector _uiHangDetector = new();

    private readonly IdentityService _identity;

    private PlaylistFolder? _aiPlaylistsFolder;
    private string? _pendingLastFmToken;
    private LastFmAuthService? _pendingLastFmAuth;
    /// <summary>
    /// FIFO list of in-flight playlist batch activities. Each PlaylistBatchStarted
    /// pushes an entry; each PlaylistBatchCompleted pops the oldest. This prevents
    /// concurrent/near-concurrent playlist downloads from orphaning toasts (the old
    /// single-field design let the second Started overwrite the handle, leaving the
    /// first toast permanently stuck at its last progress value).
    /// </summary>
    private readonly List<(LiveNotification Activity, int Total)> _playlistBatches = new();

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
            OnPropertyChanged(nameof(IsSidebarExpanded));
            OnPropertyChanged(nameof(SidebarCollapseIconKind));
            OnPropertyChanged(nameof(SidebarWidth));
        }
    }
    public bool IsSidebarExpanded => !IsSidebarCollapsed;
    public bool ShouldShowOnboarding => !_prefsService.Current.HasCompletedOnboarding;
    public Material.Icons.MaterialIconKind SidebarCollapseIconKind =>
        IsSidebarCollapsed ? Material.Icons.MaterialIconKind.ChevronRight : Material.Icons.MaterialIconKind.ChevronLeft;
    public ICommand ToggleSidebarCollapsedCommand { get; }

    public double SidebarWidth =>
        IsSidebarCollapsed ? 72 : ThemeService.Instance.SidebarWidthPx;

    private string _globalSearchQuery = string.Empty;
    public string GlobalSearchQuery
    {
        get => _globalSearchQuery;
        set
        {
            _globalSearchQuery = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasGlobalSearchQuery));
            Library.SearchQuery = value;
            Playlist.SearchQuery = value;
        }
    }

    public bool IsOnLibraryPage => CurrentPage == "Library";
    public bool IsOnRadioPage => CurrentPage == "Radio";
    public bool HasGlobalSearchQuery => !string.IsNullOrEmpty(GlobalSearchQuery);
    private LibraryViewModel ActiveLibraryVM => CurrentPage switch
    {
        "Radio" => RadioLibrary,
        "Audiobooks" => AudiobookLibrary,
        _ => Library
    };
    public int ActiveTrackCount => ActiveLibraryVM.Tracks.Count;

    public Array ToolbarSortOptions => CurrentPage == "Playlists" ? Playlist.SortOptions : ActiveLibraryVM.SortOptions;

    public SortField ToolbarCurrentSort
    {
        get => CurrentPage == "Playlists" ? Playlist.CurrentSort : ActiveLibraryVM.CurrentSort;
        set { if (CurrentPage == "Playlists") Playlist.CurrentSort = value; else ActiveLibraryVM.CurrentSort = value; }
    }

    public bool ToolbarSortAscending => CurrentPage == "Playlists" ? Playlist.SortAscending : ActiveLibraryVM.SortAscending;

    public ICommand ToolbarToggleSortDirectionCommand =>
        CurrentPage == "Playlists" ? Playlist.ToggleSortDirectionCommand : ActiveLibraryVM.ToggleSortDirectionCommand;

    public ICommand ClearGlobalSearchCommand { get; }

    private string _currentPage = "Library";
    public string CurrentPage
    {
        get => _currentPage;
        set
        {
            _currentPage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsOnLibraryPage));
            OnPropertyChanged(nameof(IsOnRadioPage));
            OnPropertyChanged(nameof(ActiveTrackCount));
            OnPropertyChanged(nameof(ToolbarSortOptions));
            OnPropertyChanged(nameof(ToolbarCurrentSort));
            OnPropertyChanged(nameof(ToolbarSortAscending));
            OnPropertyChanged(nameof(ToolbarToggleSortDirectionCommand));
            _prefsService.Update(p => p.LastPage = value);
            Nav?.SetActivePage(value);
        }
    }

    private bool _initialMoodPlaylistRun;
    private bool _isMaintenanceRunning;

    public TrackInputViewModel Input { get; }
    public LibraryViewModel Library { get; }
    public LibraryViewModel RadioLibrary { get; }
    public LibraryViewModel AudiobookLibrary { get; }
    public DownloadManagerViewModel DownloadManager { get; }
    public PlaylistViewModel Playlist { get; }
    public ExportViewModel Export { get; }
    public SettingsViewModel Settings { get; }
    public TrackDetailViewModel Detail { get; }
    public ImportViewModel Import { get; }
    public PlayerViewModel Player { get; }
    public UserProfileViewModel Profile { get; }

    public ObservableCollection<LiveNotification> ActiveToasts => ToastService.Instance.ActiveToasts;

    public ICommand PlayPlaylistCommand { get; }
    public ICommand ChangePlaylistCoverCommand { get; }
    public ICommand ClearPlaylistCoverCommand { get; }
    public ICommand ToggleDetailCommand { get; }
    public ICommand ExitCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public ICommand OpenProfileCommand { get; }
    public ICommand AboutCommand { get; }
    public ICommand OpenDataFolderCommand { get; }
    public ICommand OpenLogsCommand { get; }
    public ICommand NavigateLibraryCommand { get; }
    public ICommand NavigatePlaylistsCommand { get; }
    public ICommand NavigateRadioCommand { get; }
    public ICommand NavigateAudiobooksCommand { get; }
    public ICommand NavigateToPlaylistCommand { get; }
    public ICommand ToggleQueueCommand { get; }
    public ICommand AddTrackToPlaylistCommand { get; }
    public ICommand MovePlaylistToFolderCommand { get; }

    public NavigationViewModel Nav { get; private set; } = null!;
    public QueueViewModel Queue { get; private set; } = null!;

    public const double RightPanelWidth = 320;
    public double ActiveRightPanelWidth =>
        Detail.IsOpen || Queue.IsOpen ? RightPanelWidth : 0;

    public MainViewModel()
    {
        _identity = new IdentityService(_keyStore);
        _prefsService = new PreferencesService();
        _isSidebarCollapsed = _prefsService.Current.SidebarCollapsed;

        ThemeService.Instance.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ThemeService.Instance.SidebarWidthPx))
                Avalonia.Threading.Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(SidebarWidth)));
        };

        _secureDelete = new SecureDeleteService(_keyStore);
        _config = new ConfigService(_keyStore);
        _lastFm = new LastFmService(_config);
        _metadata = new MetadataService(_config, _lastFm);
        _spotifyBridge = new SpotifyBridgeService(_config);

        var dbService = new DatabaseService(_prefsService);
        _library = new LibraryService(dbService, _metadata, _prefsService);
        _playlists = new PlaylistService(dbService, _library);
        _library.LibraryChanged += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            Library?.Refresh();
            RadioLibrary?.Refresh();
            AudiobookLibrary?.Refresh();
            OnPropertyChanged(nameof(ActiveTrackCount));
        });

        var albumArtService = new AlbumArtService(_lastFm);
        _downloadService = new DownloadService(_library, _prefsService, albumArtService);
        DownloadManager = new DownloadManagerViewModel();
        DownloadManager.AttachService(_downloadService);
        _localAI = new LocalAIService();
        _enrichment = new LastFmEnrichmentService(_lastFm, _library, _localAI, _prefsService);
        _weatherService = new WeatherService(_keyStore);
        _moodPlaylist = new MoodPlaylistService(_weatherService, _localAI, _library);

        _plugins.Register(new YtDlpDownloadProvider(_downloadService, _prefsService));
        _plugins.Register(new LastFmMetadataProvider(_lastFm, _prefsService));
        _plugins.Register(new OpenWeatherProvider(_weatherService, _prefsService));
        _plugins.Register(new OllamaAIProvider(_localAI, _prefsService));

        Settings = new SettingsViewModel(_keyStore, _secureDelete, _prefsService, _localAI, _plugins);
        Settings.ClearYtDlpCacheRequested += OnClearYtDlpCacheRequested;

        Input = new TrackInputViewModel(_library, _metadata, _urlParser, _downloadService, _spotifyBridge, Settings, albumArtService);
        Library = new LibraryViewModel(_library, _localAI);
        Library.ExcludedMediaTypes.Add(MediaType.Radio);      // Library tab = music only
        Library.ExcludedMediaTypes.Add(MediaType.Audiobook);
        Library.Refresh();
        RadioLibrary = new LibraryViewModel(_library, _localAI) { MediaTypeFilter = MediaType.Radio };
        AudiobookLibrary = new LibraryViewModel(_library, _localAI) { MediaTypeFilter = MediaType.Audiobook };
        Library.BulkAddToPlaylistRequested += tracks => _ = AddTracksToPlaylistAsync(tracks);
        Library.AddToPlaylistRequested += track => _ = AddTracksToPlaylistAsync(new[] { track }.ToList());
        RadioLibrary.AddToPlaylistRequested += track => _ = AddTracksToPlaylistAsync(new[] { track }.ToList());
        AudiobookLibrary.AddToPlaylistRequested += track => _ = AddTracksToPlaylistAsync(new[] { track }.ToList());

        _aiPlaylistsFolder = _playlists.GetAllFolders().FirstOrDefault(f => f.Name == "AI Playlists");
        if (_aiPlaylistsFolder == null)
        {
            _aiPlaylistsFolder = _playlists.CreateFolder("AI Playlists");
        }
        Library.AiPlaylistRequested += OnAiPlaylistRequested;

        Playlist = new PlaylistViewModel(_playlists);

        Playlist.RequestCreatePlaylistName = async () =>
        {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop || desktop.MainWindow == null)
                return null;
            return await new Views.CreatePlaylistDialog().ShowDialog<string?>(desktop.MainWindow);
        };
        Playlist.RequestCreateFolderName = async () =>
        {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop || desktop.MainWindow == null)
                return null;
            return await new Views.CreateFolderDialog().ShowDialog<string?>(desktop.MainWindow);
        };

        Export = new ExportViewModel(_library, _export);
        Detail = new TrackDetailViewModel(_library, _plugins);
        Import = new ImportViewModel(_library, _metadata);
        Settings.ImportExistingLibraryRequested += () => Import.ImportFolderCommand.Execute(null);
        Player = new PlayerViewModel(_playbackService, _downloadService, _library, Settings, _metadata);
        Profile = new UserProfileViewModel(_library, _prefsService, _identity);

        Profile.TagClickedRequested += tag =>
        {
            GlobalSearchQuery = "tag:" + tag;
            CurrentPage = "Library";
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lt)
            {
                foreach (var w in lt.Windows.OfType<Views.ProfileWindow>())
                    w.Close();
            }
        };
        Profile.PlayTrackByIdRequested += id =>
        {
            var t = _library.GetAll().FirstOrDefault(t => t.Id == id);
            if (t != null) Player.PlayTrack(t);
        };

        Queue = new QueueViewModel(_library);

        Queue.PlayTrackRequested += Player.PlayTrack;
        RadioLibrary.PlayTrackRequested += Player.PlayTrack;
        AudiobookLibrary.PlayTrackRequested += Player.PlayTrack;
        RadioLibrary.TrackDetailRequested += t => Detail.OpenFor(t);
        AudiobookLibrary.TrackDetailRequested += t => Detail.OpenFor(t);

        AddTrackToPlaylistCommand = new RelayCommand<Track>(t => _ = AddTrackToPlaylistAsync(t));
        MovePlaylistToFolderCommand = new RelayCommand<Playlist>(p => _ = MovePlaylistToFolderAsync(p));

        Detail.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Detail.IsOpen))
            {
                OnPropertyChanged(nameof(ActiveRightPanelWidth));
                if (Detail.IsOpen) Queue.IsOpen = false;
            }
        };

        Queue.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Queue.IsOpen))
            {
                OnPropertyChanged(nameof(ActiveRightPanelWidth));
                if (Queue.IsOpen) Detail.IsOpen = false;
            }
        };

        Playlist.PinRequested += p => Nav.PinPlaylist(p.Id, p.Name);
        Playlist.UnpinRequested += p => Nav.UnpinPlaylist(p.Id);
        Playlist.PlayAllRequested += playlist =>
        {
            if (playlist == null || playlist.Tracks.Count == 0) return;
            Player.PlayPlaylist(playlist);
        };

        Library.NavigateToLibraryRequested += () => CurrentPage = "Library";
        Playlist.PlaylistsChanged += () => Nav.Rebuild();
        Settings.RefreshWeatherRequested += () => _ = RunMoodPlaylistAsync(forceRefresh: true);

        _localAI.FallbackNotice += message =>
        {
            Dispatcher.UIThread.Post(() =>
                ToastService.Instance.Show(message, ToastType.Warning));
        };

        // 1. Sweep Orphaned Files
        Settings.SweepOrphanedFilesRequested += dryRun =>
        {
            if (_isMaintenanceRunning) return;
            _isMaintenanceRunning = true;
            _ = Task.Run(async () =>
            {
                LiveNotification? activity = null;
                await Dispatcher.UIThread.InvokeAsync(() =>
                    activity = ToastService.Instance.StartLiveActivity(
                        dryRun ? "Previewing Orphaned Files" : "Sweeping Orphaned Files",
                        dryRun ? "Scanning for orphaned files..." : "Deleting orphaned files...",
                        isIndeterminate: true, scope: "maintenance")); // ADDED SCOPE

                try
                {
                    var dir = _prefsService.Current.DownloadDirectory;
                    if (string.IsNullOrWhiteSpace(dir)) dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nullwave", "downloads");
                    var (scanned, orphaned, deleted, failed) = await Task.Run(() => _library.SweepOrphanedFiles(dir, dryRun));

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        Settings.ReportSweepComplete(scanned, orphaned, deleted, failed, dryRun);
                        // REMOVED CompleteLiveActivity
                        _isMaintenanceRunning = false;
                    });
                }
                catch (Exception ex)
                {
                    NullActionLogger.Error(nameof(MainViewModel), ex, "SweepOrphanedFiles failed");
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        ToastService.Instance.CompleteLiveActivity(activity, "Sweep failed - check logs.", finalType: ToastType.Error);
                        _isMaintenanceRunning = false;
                    });
                }
            });
        };

        // 2. Vacuum Database
        Settings.VacuumDatabaseRequested += () =>
        {
            if (_isMaintenanceRunning) return;
            _isMaintenanceRunning = true;
            _ = Task.Run(async () =>
            {
                LiveNotification? activity = null;
                await Dispatcher.UIThread.InvokeAsync(() =>
                    activity = ToastService.Instance.StartLiveActivity("Optimizing Database", "Running VACUUM...", isIndeterminate: true, scope: "maintenance"));

                try
                {
                    var (before, after) = await Task.Run(() => _library.VacuumDatabase());

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        Settings.ReportVacuumComplete(before, after); // owns the final toast update
                        _isMaintenanceRunning = false;
                    });
                }
                catch (Exception ex)
                {
                    NullActionLogger.Error(nameof(MainViewModel), ex, "VacuumDatabase failed");
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        ToastService.Instance.CompleteLiveActivity(activity, "Database optimization failed.", finalType: ToastType.Error);
                        _isMaintenanceRunning = false;
                    });
                }
            });
        };

        // 3. Verify Links
        Settings.VerifyLinksRequested += () =>
        {
            if (_isMaintenanceRunning) return;
            _isMaintenanceRunning = true;
            _ = Task.Run(async () =>
            {
                LiveNotification? activity = null;
                await Dispatcher.UIThread.InvokeAsync(() =>
                    activity = ToastService.Instance.StartLiveActivity("Verifying Links", "Checking file links against embedded metadata...", isIndeterminate: true, scope: "maintenance")); // ADDED SCOPE

                try
                {
                    var (checkedCount, mismatches) = await Task.Run(() => _library.VerifyLinks());
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        Settings.ReportVerifyLinksComplete(checkedCount, mismatches.Count);
                        // REMOVED CompleteLiveActivity
                        _isMaintenanceRunning = false;
                    });
                }
                catch (Exception ex)
                {
                    NullActionLogger.Error(nameof(MainViewModel), ex, "VerifyLinks failed");
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        ToastService.Instance.CompleteLiveActivity(activity, "Link verification failed.", finalType: ToastType.Error);
                        _isMaintenanceRunning = false;
                    });
                }
            });
        };

        // 4. Remove Duplicates
        Settings.RemoveDuplicatesRequested += dryRun =>
        {
            if (_isMaintenanceRunning) return;
            _isMaintenanceRunning = true;
            _ = Task.Run(async () =>
            {
                LiveNotification? activity = null;
                await Dispatcher.UIThread.InvokeAsync(() =>
                    activity = ToastService.Instance.StartLiveActivity(
                        dryRun ? "Previewing Duplicates" : "Removing Duplicates",
                        dryRun ? "Scanning for duplicate tracks..." : "Removing duplicate tracks...",
                        isIndeterminate: true, scope: "maintenance")); // ADDED SCOPE

                try
                {
                    var (scanned, groups, removed) = await Task.Run(() => _library.RemoveDuplicates(dryRun));
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        Settings.ReportDedupeComplete(scanned, groups, removed, dryRun);
                        Library.Refresh();
                        Library.RefreshArtistGroups();
                        // REMOVED CompleteLiveActivity
                        _isMaintenanceRunning = false;
                    });
                }
                catch (Exception ex)
                {
                    NullActionLogger.Error(nameof(MainViewModel), ex, "RemoveDuplicates failed");
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        ToastService.Instance.CompleteLiveActivity(activity, "Deduplication failed.", finalType: ToastType.Error);
                        _isMaintenanceRunning = false;
                    });
                }
            });
        };

                // 5. Force Clean Titles
        Settings.ForceCleanTitlesRequested += () =>
        {
            if (_isMaintenanceRunning) return;
            _isMaintenanceRunning = true;
            _ = Task.Run(async () =>
            {
                LiveNotification? activity = null;
                await Dispatcher.UIThread.InvokeAsync(() =>
                    activity = ToastService.Instance.StartLiveActivity("Cleaning Titles", "Re-parsing track titles for embedded artist names...", isIndeterminate: true, scope: "maintenance")); // ADDED SCOPE

                try
                {
                    var cleaned = await Task.Run(() => _library.ForceCleanTitles());
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        Settings.ReportForceCleanComplete(cleaned);
                        Library.Refresh();
                        Library.RefreshArtistGroups();
                        // REMOVED CompleteLiveActivity
                        _isMaintenanceRunning = false;
                    });
                }
                catch (Exception ex)
                {
                    NullActionLogger.Error(nameof(MainViewModel), ex, "ForceCleanTitles failed");
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        ToastService.Instance.CompleteLiveActivity(activity, "Title cleaning failed.", finalType: ToastType.Error);
                        _isMaintenanceRunning = false;
                    });
                }
            });
        };

        // 6. Merge Similar Artists
        Settings.MergeSimilarArtistsRequested += () =>
        {
            if (_isMaintenanceRunning) return;
            _isMaintenanceRunning = true;
            _ = Task.Run(async () =>
            {
                LiveNotification? activity = null;
                await Dispatcher.UIThread.InvokeAsync(() =>
                    activity = ToastService.Instance.StartLiveActivity("Merging Artists", "Finding and merging similar artist names...", isIndeterminate: true, scope: "maintenance")); // ADDED SCOPE

                try
                {
                    var groups = await Task.Run(() => _library.FindSimilarArtistGroups());
                    int merged = 0;
                    foreach (var group in groups) merged += _library.MergeArtistGroup(group);

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        Settings.ReportArtistMergeComplete(groups.Count, merged);
                        Library.Refresh();
                        Library.RefreshArtistGroups();
                        // REMOVED CompleteLiveActivity
                        _isMaintenanceRunning = false;
                    });
                }
                catch (Exception ex)
                {
                    NullActionLogger.Error(nameof(MainViewModel), ex, "MergeSimilarArtists failed");
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        ToastService.Instance.CompleteLiveActivity(activity, "Artist merge failed.", finalType: ToastType.Error);
                        _isMaintenanceRunning = false;
                    });
                }
            });
        };

        Settings.AIFeaturesEnabledChanged += enabled =>
        {
            if (!enabled)
            {
                Settings.StopHealthCheck();
                Log.Information("[MainViewModel] AI features disabled - health check stopped");
                ToastService.Instance.Show("Local AI models offline.", ToastType.Info, scope: "ai");
            }
            else
            {
                var activity = ToastService.Instance.StartLiveActivity("Local AI Subsystem", "Pinging background inference node server...", isIndeterminate: true, scope: "ai");

                _ = Task.Run(async () =>
                {
                    bool running = await _localAI.PingAsync();
                    Dispatcher.UIThread.Post(() =>
                    {
                        ToastService.Instance.Show(
                            running ? "Local LLM engine connected successfully!" : "Could not reach local AI server. Check configurations.",
                            running ? ToastType.Success : ToastType.Warning,
                            scope: "ai"); // Updates the live activity instead of spawning a second toast
                        Settings.SetAIServiceState(running ? AIServiceState.Running : AIServiceState.Stopped);
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
            Dispatcher.UIThread.Post(() =>
            {
                Settings.PowerStateLabel = state switch
                {
                    PowerState.AC => LocalizationService.Instance["Settings_Dynamic_Power_PluggedIn"],
                    PowerState.Battery => LocalizationService.Instance["Settings_Dynamic_Power_OnBattery"],
                    _ => LocalizationService.Instance["Settings_Dynamic_Power_UnknownShort"]
                };
            });
        };

        Settings.PowerStateLabel = PowerStateService.ReadPowerState() switch
        {
            PowerState.AC => LocalizationService.Instance["Settings_Dynamic_Power_PluggedIn"],
            PowerState.Battery => LocalizationService.Instance["Settings_Dynamic_Power_OnBattery"],
            _ => LocalizationService.Instance["Settings_Dynamic_Power_UnknownShort"]
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
            Dispatcher.UIThread.Post(() =>
            {
                Library.Refresh();
                Library.RefreshArtistGroups();
                if (isInteractive)
                    ToastService.Instance.Show("Track download completed successfully.", ToastType.Success, scope: "download");
            });
        };

        _downloadService.DownloadFailed += (_, _, isInteractive) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                Library.Refresh();
                if (isInteractive)
                    ToastService.Instance.Show("A track download failed. Check your connection or logs.", ToastType.Error, scope: "download");
            });
        };

        _downloadService.PlaylistBatchStarted += totalTracks =>
        {
            var activity = ToastService.Instance.StartLiveActivity(
                "Downloading Playlist",
                totalTracks > 0
                    ? $"Downloading Playlist: 0/{totalTracks} (Skipped: 0)..."
                    : "Fetching playlist metadata...",
                isIndeterminate: totalTracks == 0
            );
            lock (_playlistBatches)
            {
                _playlistBatches.Add((activity, totalTracks));
            }
        };

        _downloadService.PlaylistBatchProgress += (completed, total, skipped) =>
        {
            LiveNotification? activity;
            lock (_playlistBatches)
            {
                // Match by total track count so progress updates route to the right toast
                // even when two playlist downloads run concurrently.
                var match = _playlistBatches.FirstOrDefault(b => b.Total == total && b.Total > 0);
                activity = match != default ? match.Activity
                         : _playlistBatches.LastOrDefault().Activity;
            }
            if (activity == null) return;

            var doneSoFar = completed + skipped;
            ToastService.Instance.UpdateLiveActivity(
                activity,
                message: $"Downloading Playlist: {completed}/{total} (Skipped: {skipped})...",
                progressValue: total > 0 ? doneSoFar * 100.0 / total : 0,
                isIndeterminate: false
            );
        };

        _downloadService.PlaylistBatchCompleted += (completed, failed, skipped) =>
        {
            LiveNotification? activity;
            lock (_playlistBatches)
            {
                activity = _playlistBatches.Count > 0 ? _playlistBatches[0].Activity : null;
                if (_playlistBatches.Count > 0) _playlistBatches.RemoveAt(0);
            }

            var summary = $"Bulk download complete: {completed} downloaded, {failed} unavailable, {skipped} duplicates skipped.";
            ToastService.Instance.CompleteLiveActivity(activity, summary);
            Dispatcher.UIThread.Post(() => Library.Refresh());
            Log.Information("[MainViewModel] {Summary}", summary);
        };

        Player.UpdateSkipPenaltyCap(Settings.SkipPenaltyCap);
        Input.TrackMetadataUpdated += Library.Refresh;
        Library.TrackDetailRequested += track => Detail.OpenFor(track);
        Library.PlayTrackRequested += Player.PlayTrack;
        Import.ImportCompleted += () => { Library.Refresh(); Library.RefreshArtistGroups(); };

        Input.PlaylistImportRequested += (playlistUrl) =>
        {
            if (_plugins.Get<YtDlpDownloadProvider>() is not { } ytDlpProvider || !ytDlpProvider.SupportsUrl(playlistUrl))
            {
                Log.Information("[MainViewModel] Playlist download skipped - yt-dlp plugin unavailable/disabled");
                ToastService.Instance.Show("Downloads are disabled. Enable yt-dlp in Settings to import playlists.", ToastType.Warning);
                return;
            }

            Log.Information("[MainViewModel] Intercepted playlist URL early, starting bulk download");
            _ = _downloadService.DownloadPlaylistAsync(
                playlistUrl: playlistUrl,
                onTrackReady: (downloadedTrack) =>
                {
                    if (_plugins.Get<LastFmMetadataProvider>() is { })
                        _enrichment.EnrichAsync(downloadedTrack);
                    Dispatcher.UIThread.Post(() => Library.Refresh());
                });
        };

            Input.RadioSiteRequested += (stationName, channels) => _ = HandleRadioSiteAsync(stationName, channels);

            async Task HandleRadioSiteAsync(string stationName, IReadOnlyList<RadioChannel> channels)
            {
                if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop || desktop.MainWindow == null) return;
                var chosen = await new Views.Dialogs.RadioMoodDialog(channels).ShowDialog<List<RadioChannel>>(desktop.MainWindow);
                if (chosen == null || chosen.Count == 0) return;

                int added = 0;
                foreach (var channel in chosen)
                {
                    var stream = channel.StreamUrl;
                    if (string.IsNullOrWhiteSpace(stream)) stream = await RadioStreamResolver.ResolveAsync($"{stationName} {channel.Name}");
                    if (string.IsNullOrWhiteSpace(stream))
                    {
                        ToastService.Instance.Show(string.Format(LocalizationService.Instance["Radio_Resolved_Failed"], channel.Name), ToastType.Warning);
                        continue;
                    }

                    _library.Add(new Track
                    {
                        Title = $"{stationName} - {channel.Name}",
                        Artist = stationName,
                        Url = stream,
                        Source = TrackSource.Unknown,
                        MediaType = MediaType.Radio
                    });
                    added++;
                }

                Library.Refresh();
                RadioLibrary.Refresh();
                if (added > 0) ToastService.Instance.Show(string.Format(LocalizationService.Instance["Radio_StationAdded"], added), ToastType.Success);
            }

        Input.TrackAdded += () =>
        {
            var track = _library.GetAll().LastOrDefault();
            if (track == null) return;
            if (track.MediaType != MediaType.Radio && _plugins.Get<LastFmMetadataProvider>() is { })
            {
                _enrichment.EnrichAsync(track);
            }
            Dispatcher.UIThread.Post(() => Library.Refresh());
        };

        _enrichment.BackfillCompleted += () =>
        {
            if (_initialMoodPlaylistRun) return;
            _initialMoodPlaylistRun = true;

            if (_prefsService.Current.AutoGenerateMoodPlaylist)
                _ = RunMoodPlaylistAsync(forceRefresh: false);
            else
                Log.Information("[MainViewModel] Auto Mood Mix disabled - skipping startup generation.");
        };

        _ = Task.Run(async () =>
        {
            await Task.Delay(3000);
            if (!_prefsService.Current.AutoGenerateMoodPlaylist) return;

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
            if (_plugins.Get<LastFmMetadataProvider>() is not { } lastFmProvider || !lastFmProvider.IsConfiguredForScrobbling)
            {
                Log.Debug("[MainViewModel] Scrobble requested but Last.fm plugin not configured/available");
                return;
            }

            var success = await lastFmProvider.ScrobbleAsync(title, artist, playedAt);
            if (!success)
            {
                Log.Warning("[MainViewModel] Scrobble failed for '{Title}' by '{Artist}'", title, artist);
                ToastService.Instance.Show($"Scrobble failed for '{title}' by {artist}.", ToastType.Warning, scope: "scrobble");
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
                _pendingLastFmAuth = auth;
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
                Settings.ReportLastFmAuthFailed("No pending authorization - click Connect first.");
                return;
            }

            var result = await _pendingLastFmAuth.GetSessionKeyAsync(_pendingLastFmToken);
            if (!result.Success)
            {
                Settings.ReportLastFmAuthFailed(
                    result.Error ?? "Authorization not yet granted - approve access in your browser first.");
                return;
            }

            _keyStore.SaveKey("LastFm:SessionKey", result.SessionKey);
            _keyStore.SaveKey("LastFm:Username", result.Username);
            _pendingLastFmToken = null;
            _pendingLastFmAuth = null;

            _lastFm = new LastFmService(_config);
            Settings.ReportLastFmConnected(result.Username);
            ToastService.Instance.Show($"Successfully connected to Last.fm as {result.Username}!", ToastType.Success, scope: "lastfm");
        };

        Settings.LastFmDisconnectRequested += () =>
        {
            _keyStore.DeleteKey("LastFm:SessionKey");
            _keyStore.DeleteKey("LastFm:Username");
            _lastFm = new LastFmService(_config);
            Settings.ReportLastFmDisconnected();
            ToastService.Instance.Show("Disconnected from Last.fm accounts.", ToastType.Info, scope: "lastfm");
        };

        // 7. Clear Thumbnails
        Settings.ClearThumbnailsRequested += () =>
        {
            if (_isMaintenanceRunning) return;
            _isMaintenanceRunning = true;
            _ = Task.Run(async () =>
            {
                LiveNotification? activity = null;
                await Dispatcher.UIThread.InvokeAsync(() =>
                    activity = ToastService.Instance.StartLiveActivity("Clearing Artwork", "Purging cached thumbnails and resetting index registers...", isIndeterminate: true, scope: "maintenance")); // ADDED SCOPE

                try
                {
                    int cleared = await Task.Run(() => { var count = _library.GetAll().Count(t => !string.IsNullOrEmpty(t.AlbumArtPath)); _library.ClearAllArt(); return count; });
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        Settings.ReportThumbnailsCleared(cleared);
                        _library.RebackfillThumbnails();
                        // REMOVED CompleteLiveActivity
                    });

                    await Task.Delay(500);
                    _enrichment.BackfillAsync();
                    await Task.Delay(1500);

                    await Dispatcher.UIThread.InvokeAsync(() => { Library.Refresh(); _isMaintenanceRunning = false; });
                }
                catch (Exception ex)
                {
                    NullActionLogger.Error(nameof(MainViewModel), ex, "ClearThumbnails failed");
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        ToastService.Instance.CompleteLiveActivity(activity, "Artwork collection reset failed.", finalType: ToastType.Error);
                        _isMaintenanceRunning = false;
                    });
                }
            });
        };

        // 8. Repair Paths
        Settings.RepairPathsRequested += () =>
        {
            if (_isMaintenanceRunning) return;
            _isMaintenanceRunning = true;
            _ = Task.Run(async () =>
            {
                LiveNotification? activity = null;
                await Dispatcher.UIThread.InvokeAsync(() =>
                    activity = ToastService.Instance.StartLiveActivity("Repairing Paths", "Scanning local track library indices for broken file links...", isIndeterminate: true, scope: "maintenance")); // ADDED SCOPE

                try
                {
                    var (total, missing, removed) = await Task.Run(() => _library.RepairPaths(removeDeadEntries: true));
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        Library.Refresh();
                        Settings.ReportRepairPathsComplete(total, missing, removed);
                        // REMOVED CompleteLiveActivity
                        _isMaintenanceRunning = false;
                    });
                }
                catch (Exception ex)
                {
                    NullActionLogger.Error(nameof(MainViewModel), ex, "RepairPaths failed");
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        ToastService.Instance.CompleteLiveActivity(activity, "Path repair failed.", finalType: ToastType.Error);
                        _isMaintenanceRunning = false;
                    });
                }
            });
        };

        // 9. Reimport Assets
        Settings.ReimportAssetsRequested += () =>
        {
            if (_isMaintenanceRunning) return;
            _isMaintenanceRunning = true;
            _ = Task.Run(async () =>
            {
                LiveNotification? activity = null;
                await Dispatcher.UIThread.InvokeAsync(() =>
                    activity = ToastService.Instance.StartLiveActivity("Validating Assets", "Scanning repository directories to map unlinked tracks...", isIndeterminate: true, scope: "maintenance")); // ADDED SCOPE

                try
                {
                    var dir = _prefsService.Current.DownloadDirectory;
                    if (string.IsNullOrWhiteSpace(dir)) dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nullwave", "downloads");
                    var relinked = await Task.Run(() => _library.ReimportAssets(dir));

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        Library.Refresh();
                        Settings.ReportReimportComplete(relinked);
                        // REMOVED CompleteLiveActivity
                        _isMaintenanceRunning = false;
                    });
                }
                catch (Exception ex)
                {
                    NullActionLogger.Error(nameof(MainViewModel), ex, "ReimportAssets failed");
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        ToastService.Instance.CompleteLiveActivity(activity, "Asset reimport failed.", finalType: ToastType.Error);
                        _isMaintenanceRunning = false;
                    });
                }
            });
        };

        // 10. Force Meta Resync
        Settings.ForceMetaResyncRequested += () =>
        {
            if (_isMaintenanceRunning) return;
            _isMaintenanceRunning = true;
            _ = Task.Run(async () =>
            {
                LiveNotification? activity = null;
                await Dispatcher.UIThread.InvokeAsync(() =>
                    activity = ToastService.Instance.StartLiveActivity("Syncing Metadata", "Flushing local tag database cache registers...", isIndeterminate: true, scope: "maintenance")); // ADDED SCOPE

                try
                {
                    int cleared = await Task.Run(() => _library.ClearTagsForReSync());
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        Settings.ReportMetaResyncComplete(cleared);
                        Library.Refresh();
                        // REMOVED CompleteLiveActivity
                    });

                    await Task.Delay(800);
                    _enrichment.BackfillAsync();
                    await Task.Delay(1200);
                    await Dispatcher.UIThread.InvokeAsync(() => { _isMaintenanceRunning = false; });
                }
                catch (Exception ex)
                {
                    NullActionLogger.Error(nameof(MainViewModel), ex, "ForceMetaResync failed");
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        Settings.ReportRepairFailed("Force Meta Re-sync", ex.Message);
                        // REMOVED CompleteLiveActivity (ReportRepairFailed handles it)
                        _isMaintenanceRunning = false;
                    });
                }
            });
        };

        // 11. Sync Files
        Settings.SyncFilesRequested += dryRun =>
        {
            if (_isMaintenanceRunning) return;
            _isMaintenanceRunning = true;
            _ = Task.Run(async () =>
            {
                LiveNotification? activity = null;
                await Dispatcher.UIThread.InvokeAsync(() =>
                    activity = ToastService.Instance.StartLiveActivity("Sync Files", dryRun ? "Previewing file sync..." : "Syncing files with library...", true, scope: "maintenance")); // ADDED SCOPE
                try
                {
                    string? playing = null;
                    await Dispatcher.UIThread.InvokeAsync(() => playing = Player.CurrentTrack?.FilePath);
                    var r = _library.SyncLocalFilesWithLibrary(dryRun, playing);
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        Settings.ReportSyncFilesComplete(r, dryRun);
                        // REMOVED CompleteLiveActivity
                        Library.Refresh();
                        _isMaintenanceRunning = false;
                    });
                }
                catch (Exception ex)
                {
                    NullActionLogger.Error(nameof(MainViewModel), ex, "SyncFiles failed");
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        ToastService.Instance.CompleteLiveActivity(activity, "File sync failed.", finalType: ToastType.Error);
                        _isMaintenanceRunning = false;
                    });
                }
            });
        };

        // 12. Backfill Durations
        Settings.BackfillDurationsRequested += () =>
        {
            if (_isMaintenanceRunning) return;
            _isMaintenanceRunning = true;
            _ = Task.Run(async () =>
            {
                LiveNotification? activity = null;
                await Dispatcher.UIThread.InvokeAsync(() =>
                    activity = ToastService.Instance.StartLiveActivity("Backfilling Durations", "Scanning local files for missing track lengths...", isIndeterminate: true, scope: "maintenance")); // ADDED SCOPE

                try
                {
                    int updated = await Task.Run(() => _library.BackfillDurations());
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        Settings.ReportBackfillDurationsComplete(updated);
                        // REMOVED CompleteLiveActivity
                        _isMaintenanceRunning = false;
                    });
                }
                catch (Exception ex)
                {
                    NullActionLogger.Error(nameof(MainViewModel), ex, "BackfillDurations failed");
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        ToastService.Instance.CompleteLiveActivity(activity, "Duration backfill failed.", finalType: ToastType.Error);
                        _isMaintenanceRunning = false;
                    });
                }
            });
        };

        // 13. Restore Database from Backup
        Settings.RestoreDatabaseRequested += () =>
        {
            if (_isMaintenanceRunning) return;
            _isMaintenanceRunning = true;
            _ = Task.Run(async () =>
            {
                LiveNotification? activity = null;
                await Dispatcher.UIThread.InvokeAsync(() =>
                    activity = ToastService.Instance.StartLiveActivity("Restoring Database", "Please select a backup file...", isIndeterminate: true, scope: "maintenance"));

                try
                {
                    string? selectedFile = null;
                    await Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
                        {
                            var files = await desktop.MainWindow.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
                            {
                                Title = "Select Database Backup (.db)",
                                AllowMultiple = false,
                                FileTypeFilter = new[] { new Avalonia.Platform.Storage.FilePickerFileType("SQLite Backup") { Patterns = new[] { "*.db" } } }
                            });
                            if (files.Count > 0) selectedFile = files[0].Path.LocalPath;
                        }
                    });

                    if (string.IsNullOrEmpty(selectedFile))
                    {
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            ToastService.Instance.CompleteLiveActivity(activity, "Restore cancelled.", finalType: ToastType.Info);
                            _isMaintenanceRunning = false;
                        });
                        return;
                    }

                    string targetPath = NullWavePaths.DatabasePath;
                    string tempPath = targetPath + ".restoring";
                    File.Copy(selectedFile, tempPath, overwrite: true);

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        ToastService.Instance.Show("Backup staged! Please restart NullWave to apply the restored database.", ToastType.Success, 10000, scope: "maintenance");
                        _isMaintenanceRunning = false;
                    });
                }
                catch (Exception ex)
                {
                    NullActionLogger.Error(nameof(MainViewModel), ex, "RestoreDatabase failed");
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        ToastService.Instance.CompleteLiveActivity(activity, "Restore failed.", finalType: ToastType.Error);
                        _isMaintenanceRunning = false;
                    });
                }
            });
        };

        Settings.GenerateMoodPlaylistRequested += () => _ = RunMoodPlaylistAsync(forceRefresh: true);
        Settings.GenerateTagMoodPlaylistRequested += () => _ = RunMoodPlaylistAsync(forceRefresh: true, forceTags: true);

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
                        Title = "Import AI Tag JSON",
                        AllowMultiple = false,
                        FileTypeFilter = new[]
                        {
                            new Avalonia.Platform.Storage.FilePickerFileType("JSON / Text") { Patterns = new[] { "*.json", "*.txt", "*.md" } },
                            new Avalonia.Platform.Storage.FilePickerFileType("All files") { Patterns = new[] { "*" } },
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

                    track.Tags ??= new List<string>();
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
            Library.ShowAll();
            CurrentPage = "Library";
            LogNav("Library");
        });

        NavigateRadioCommand = new RelayCommand(() =>
        {
            CurrentPage = "Radio";
            LogNav("Radio");
        });

        NavigateAudiobooksCommand = new RelayCommand(() =>
        {
            CurrentPage = "Audiobooks";
            LogNav("Audiobooks");
        });

        NavigatePlaylistsCommand = new RelayCommand(() =>
        {
            CurrentPage = "Playlists";
            Playlist.SelectFirst();
            Nav.SetPlaylistActive(Playlist.SelectedPlaylist?.Id);
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
            Queue.IsOpen = !Queue.IsOpen;
            LogNav("Queue");
        });

        Nav = new NavigationViewModel(
            _prefsService, _playlists,
            NavigateLibraryCommand, NavigatePlaylistsCommand, NavigateRadioCommand, NavigateAudiobooksCommand,
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
        ToggleSidebarCollapsedCommand = new RelayCommand(() =>
        {
            IsSidebarCollapsed = !IsSidebarCollapsed;
            _prefsService.Update(p => p.SidebarCollapsed = IsSidebarCollapsed);
        });
        ClearGlobalSearchCommand = new RelayCommand(() => GlobalSearchQuery = string.Empty);

        PlayPlaylistCommand = new RelayCommand<Playlist>(PlayPlaylist);
        ChangePlaylistCoverCommand = new RelayCommand<Playlist>(p => _ = ChangePlaylistCoverAsync(p));
        ClearPlaylistCoverCommand = new RelayCommand<Playlist>(ClearPlaylistCover);
        ToggleDetailCommand = new RelayCommand(ToggleDetail);

        Playlist.AttachNavigation(Nav);

        CurrentPage = "Library";

        _ = RunStartupDiagnosticsAsync();
    }

    public void DisposePowerState() => _powerState.Dispose();

    public async Task UnloadAIModelAsync()
    {
        if (string.IsNullOrWhiteSpace(_localAI.CurrentModel)) return;

        try
        {
            await _localAI.UnloadModelAsync(_localAI.CurrentModel);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[MainViewModel] Failed to unload AI model on exit");
        }
    }

    private async Task RunStartupDiagnosticsAsync()
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
                    ToastService.Instance.Show("yt-dlp cache cleared successfully!", ToastType.Success, 5000, scope: "maintenance");
                    Log.Information("[MainViewModel] yt-dlp cache cleared successfully");
                }
                else
                {
                    ToastService.Instance.Show($"Failed to clear cache: {error}", ToastType.Error, 5000, scope: "maintenance");
                    Log.Warning("[MainViewModel] yt-dlp cache clear failed: {Error}", error);
                }
            }
        }
        catch (Exception ex)
        {
            ToastService.Instance.Show($"Failed to clear cache: {ex.Message}", ToastType.Error, 5000, scope: "maintenance");
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

        private async Task RunMoodPlaylistAsync(bool forceRefresh, bool forceTags = false)
    {
        // Added scope: "mood-gen" to prevent duplicate toasts if triggered multiple times concurrently
        var activity = ToastService.Instance.StartLiveActivity(
            "Mood Playlist",
            "Fetching weather data...",
            isIndeterminate: true,
            scope: "mood-gen"
        );

        try
        {
            if (_plugins.Get<IWeatherProvider>() is not { } weatherProvider)
            {
                ToastService.Instance.CompleteLiveActivity(activity, "Weather plugin unavailable.", finalType: ToastType.Warning);
                Log.Information("[MainViewModel] OpenWeather plugin unavailable/disabled - skipping mood playlist");
                return;
            }

            var lat = Settings.Latitude;
            var lon = Settings.Longitude;

            if (lat == 0 && lon == 0)
            {
                ToastService.Instance.CompleteLiveActivity(activity, "No location set.", finalType: ToastType.Warning);
                Settings.ReportMoodPlaylistFailed("No location set - add coordinates in Settings → Smart Sorting");
                return;
            }

            _localAI.CurrentModel = Settings.SelectedModel;
            bool useAi = !forceTags && Settings.AIFeaturesEnabled && Settings.UseLocalAI;

            ToastService.Instance.UpdateLiveActivity(activity, message: "Matching mood tags...");
            var result = await _moodPlaylist.GenerateAsync(lat, lon, useAi, forceRefresh);

            if (!result.Success)
            {
                ToastService.Instance.CompleteLiveActivity(activity, $"Failed: {result.FailureReason}", finalType: ToastType.Error);
                Settings.ReportMoodPlaylistFailed(result.FailureReason ?? "Unknown error");
                return;
            }

            ToastService.Instance.UpdateLiveActivity(activity, message: "Creating playlist...");
            var oldMoodPlaylists = _playlists.GetAll()
                .Where(p => p.Name.StartsWith("Mood:") || p.Name == "Mood Mix")
                .ToList();

            bool wasPinned = oldMoodPlaylists.Any(p => Nav.IsPlaylistPinned(p.Id));
            foreach (var old in oldMoodPlaylists)
                _playlists.Remove(old.Id);

            var playlist = _playlists.Create("Mood Mix",
                $"{result.Mood} · {result.WeatherCondition}, {result.TemperatureC:F0}°C · " +
                $"auto-generated {(result.UsedAI ? "by local AI" : "from tags")} on {DateTime.Now:dd MMM, HH:mm}");

            foreach (var track in result.Tracks)
                _playlists.AddTrack(playlist.Id, track);

            if (wasPinned)
                Nav.PinPlaylist(playlist.Id, playlist.Name);

            Playlist.Refresh();
            Nav.RefreshPlaylistLists();

            Settings.ReportMoodPlaylistGenerated(result.Tracks.Count, result.Mood);
            
            // CompleteLiveActivity automatically updates the existing scoped toast and schedules it for dismissal
            ToastService.Instance.CompleteLiveActivity(activity, $"Generated 'Mood Mix' ({result.Tracks.Count} tracks).");
            Log.Information("[MainViewModel] Mood playlist generated: Mood Mix ({Count} tracks, AI={UsedAI})",
                result.Tracks.Count, result.UsedAI);
        }
        catch (Exception ex)
        {
            ToastService.Instance.CompleteLiveActivity(activity, "Mood playlist generation failed.", finalType: ToastType.Error);
            Settings.ReportMoodPlaylistFailed(ex.Message);
            NullActionLogger.Error(nameof(MainViewModel), ex, "Mood playlist generation failed");
        }
    }

        private void OnAiPlaylistRequested(string query, List<Track> tracks)
    {
        if (tracks.Count == 0) return;

        var playlistName = $"AI: {query}";
        if (_playlists.NameExists(playlistName))
        {
            playlistName = $"AI: {query} ({DateTime.Now:HH:mm})";
        }

        var playlist = _playlists.Create(playlistName, $"Generated by AI prompt: {query}", _aiPlaylistsFolder?.Id);

        foreach (var track in tracks)
        {
            _playlists.AddTrack(playlist.Id, track);
        }

        Playlist.Refresh();
        Playlist.SelectById(playlist.Id);
        Nav.RefreshPlaylistLists();   // refresh sidebar folder counts + child rows immediately
        CurrentPage = "Playlists";

        // "ai: ..." is a command, not a filter - clear the search box so the
        // freshly created playlist opens fully visible instead of being filtered
        // down to 0 tracks by its own prompt text.
        Dispatcher.UIThread.Post(() => GlobalSearchQuery = string.Empty);

        Log.Information("[MainViewModel] AI playlist created: {Name} ({Count} track{Suffix})",
            playlistName, tracks.Count, tracks.Count == 1 ? "" : "s");
    }

    private void PlayPlaylist(Playlist? playlist)
    {
        if (playlist == null || playlist.Tracks.Count == 0) return;
        Player.PlayPlaylist(playlist);
    }

    private async Task ChangePlaylistCoverAsync(Playlist? playlist)
    {
        if (playlist == null) return;
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop || desktop.MainWindow == null) return;

        var files = await desktop.MainWindow.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Choose playlist cover",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new Avalonia.Platform.Storage.FilePickerFileType("Images") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.webp" } }
            }
        });

        if (files.Count == 0) return;

        playlist.CustomArtPath = files[0].Path.LocalPath;
        _playlists.UpdatePlaylist(playlist);
        Playlist.Refresh();
        Nav.RefreshPlaylistLists();
        ToastService.Instance.Show($"Cover updated for '{playlist.Name}'.", ToastType.Success, scope: "playlist");
    }

    private void ClearPlaylistCover(Playlist? playlist)
    {
        if (playlist == null) return;
        playlist.CustomArtPath = null;
        _playlists.UpdatePlaylist(playlist);
        Playlist.Refresh();
        Nav.RefreshPlaylistLists();
    }

    private void ToggleDetail()
    {
        if (Detail.IsOpen) Detail.IsOpen = false;
        else if (Player.CurrentTrack != null) Detail.OpenFor(Player.CurrentTrack);
    }

    private async Task AddTracksToPlaylistAsync(List<Track> tracks)
    {
        if (tracks.Count == 0) return;
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop || desktop.MainWindow == null) return;

        var playlist = await new Views.AddToPlaylistDialog(_playlists).ShowDialog<Playlist?>(desktop.MainWindow);
        if (playlist == null) return;

        int added = 0;
        foreach (var t in tracks)
            if (_playlists.AddTrack(playlist.Id, t)) added++;

        ToastService.Instance.Show(
            added > 0 ? $"Added {added} track(s) to '{playlist.Name}'."
                    : $"All tracks already in '{playlist.Name}'.",
            added > 0 ? ToastType.Success : ToastType.Info, scope: "playlist");
        Playlist.Refresh();
        Nav.RefreshPlaylistLists();
    }

    private async Task AddTrackToPlaylistAsync(Track? track)
    {
        if (track == null) return;
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop || desktop.MainWindow == null) return;

        var playlist = await new Views.AddToPlaylistDialog(_playlists).ShowDialog<Playlist?>(desktop.MainWindow);
        if (playlist == null) return;

        if (_playlists.AddTrack(playlist.Id, track))
        {
            ToastService.Instance.Show($"Added '{track.Title}' to '{playlist.Name}'.", ToastType.Success, scope: "playlist");
            Playlist.Refresh();
            Nav.RefreshPlaylistLists();
        }
        else
        {
            ToastService.Instance.Show($"'{track.Title}' is already in '{playlist.Name}'.", ToastType.Info, scope: "playlist");
        }
    }

    private async Task MovePlaylistToFolderAsync(Playlist? playlist)
    {
        if (playlist == null) return;
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop || desktop.MainWindow == null) return;

        var choice = await new Views.MoveToFolderDialog(_playlists, playlist.FolderId).ShowDialog<Views.FolderOption?>(desktop.MainWindow);
        if (choice == null) return;

        Nav.MovePlaylistToFolder(playlist.Id, choice.FolderId);
        ToastService.Instance.Show(choice.FolderId == null
            ? $"'{playlist.Name}' moved to top level."
            : $"'{playlist.Name}' moved to '{choice.Name}'.", ToastType.Success, scope: "playlist");
    }

    private static void LogNav(string destination)
        => NullActionLogger.User("Navigate", destination, nameof(MainViewModel));
}
