using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using NullWave.Helpers;
using NullWave.Helpers.Logging;
using NullWave.Models;
using NullWave.Services;
using NullWave.Services.Integration;
using NullWave.Services.Plugins;
using NullWave.Services.Security;
using NullWave.Services.SmartSorting;
using NullWave.ViewModels.Base;
using Serilog;

namespace NullWave.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    // --- Fields & Services ---
    // FIX: Removed 'readonly' from fields initialized in MainViewModel.Integration.cs
    private readonly KeyStoreService _keyStore = new();
    private SecureDeleteService _secureDelete = null!;
    private ConfigService _config = null!;
    private LibraryService _library = null!;
    private PlaylistService _playlists = null!;
    private LastFmService _lastFm = null!;
    private MetadataService _metadata = null!;
    private readonly UrlParserService _urlParser = new();
    private readonly ExportService _export = new();
    private readonly PlaybackService _playbackService = new();
    private DownloadService _downloadService = null!;
    private SpotifyBridgeService _spotifyBridge = null!;
    private PreferencesService _prefsService = null!;
    private LastFmEnrichmentService _enrichment = null!;
    private WeatherService _weatherService = null!;
    private LocalAIService _localAI = null!;
    private MoodPlaylistService _moodPlaylist = null!;
    private PowerStateService _powerState = null!;
    private PluginManager _plugins = null!;
    private IdentityService _identity = null!;
    
    private PlaylistFolder? _aiPlaylistsFolder;
    private string? _pendingLastFmToken;
    private LastFmAuthService? _pendingLastFmAuth;
    private readonly List<(LiveNotification Activity, int Total)> _playlistBatches = new();
    private bool _initialMoodPlaylistRun;
    private bool _isMaintenanceRunning;

    // --- UI State Properties ---
    private bool _isMenuBarVisible;
    public bool IsMenuBarVisible { get => _isMenuBarVisible; set { _isMenuBarVisible = value; OnPropertyChanged(); } }
    public void ToggleMenuBar() => IsMenuBarVisible = !IsMenuBarVisible;

    private bool _isCustomizingSidebar;
    public bool IsCustomizingSidebar { get => _isCustomizingSidebar; set { _isCustomizingSidebar = value; OnPropertyChanged(); } }

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

    public double SidebarWidth => IsSidebarCollapsed ? 72 : ThemeService.Instance.SidebarWidthPx;

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
    public bool HasGlobalSearchQuery => !string.IsNullOrEmpty(GlobalSearchQuery);

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
    public bool IsOnLibraryPage => CurrentPage == "Library";
    public bool IsOnRadioPage => CurrentPage == "Radio";

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

    // --- Child ViewModels ---
    public TrackInputViewModel Input { get; private set; } = null!;
    public LibraryViewModel Library { get; private set; } = null!;
    public LibraryViewModel RadioLibrary { get; private set; } = null!;
    public LibraryViewModel AudiobookLibrary { get; private set; } = null!;
    public DownloadManagerViewModel DownloadManager { get; private set; } = null!;
    public PlaylistViewModel Playlist { get; private set; } = null!;
    public ExportViewModel Export { get; private set; } = null!;
    public SettingsViewModel Settings { get; private set; } = null!;
    public TrackDetailViewModel Detail { get; private set; } = null!;
    public ImportViewModel Import { get; private set; } = null!;
    public PlayerViewModel Player { get; private set; } = null!;
    public UserProfileViewModel Profile { get; private set; } = null!;
    public NavigationViewModel Nav { get; private set; } = null!;
    public QueueViewModel Queue { get; private set; } = null!;

    public ObservableCollection<LiveNotification> ActiveToasts => ToastService.Instance.ActiveToasts;
    public const double RightPanelWidth = 320;
    public double ActiveRightPanelWidth => Detail.IsOpen || Queue.IsOpen ? RightPanelWidth : 0;

    // --- Commands ---
    public ICommand ToggleCustomizeSidebarCommand { get; private set; } = null!;
    public ICommand ToggleSidebarCollapsedCommand { get; private set; } = null!;
    public ICommand ClearGlobalSearchCommand { get; private set; } = null!;
    public ICommand PlayPlaylistCommand { get; private set; } = null!;
    public ICommand ChangePlaylistCoverCommand { get; private set; } = null!;
    public ICommand ClearPlaylistCoverCommand { get; private set; } = null!;
    public ICommand ToggleDetailCommand { get; private set; } = null!;
    public ICommand ExitCommand { get; private set; } = null!;
    public ICommand OpenSettingsCommand { get; private set; } = null!;
    public ICommand OpenProfileCommand { get; private set; } = null!;
    public ICommand AboutCommand { get; private set; } = null!;
    public ICommand OpenDataFolderCommand { get; private set; } = null!;
    public ICommand OpenLogsCommand { get; private set; } = null!;
    public ICommand NavigateLibraryCommand { get; private set; } = null!;
    public ICommand NavigatePlaylistsCommand { get; private set; } = null!;
    public ICommand NavigateRadioCommand { get; private set; } = null!;
    public ICommand NavigateAudiobooksCommand { get; private set; } = null!;
    public ICommand NavigateToPlaylistCommand { get; private set; } = null!;
    public ICommand ToggleQueueCommand { get; private set; } = null!;
    public ICommand AddTrackToPlaylistCommand { get; private set; } = null!;
    public ICommand MovePlaylistToFolderCommand { get; private set; } = null!;

    // --- Constructor ---
    public MainViewModel()
    {
        InitializeServices();
        InitializeChildViewModels();
        InitializeCommands();
        WireCoreEvents();
        WireMaintenanceEvents();
        WireIntegrationEvents();
        WireAIAndPowerEvents();
        
        Nav.SetActivePage(CurrentPage);
        Library.RefreshArtistGroups();
        CurrentPage = "Library";
        
        _ = RunStartupDiagnosticsAsync();
    }

    public void DisposePowerState() => _powerState.Dispose();

    public async Task UnloadAIModelAsync()
    {
        if (string.IsNullOrWhiteSpace(_localAI.CurrentModel)) return;
        try { await _localAI.UnloadModelAsync(_localAI.CurrentModel); }
        catch (Exception ex) { Log.Warning(ex, "[MainViewModel] Failed to unload AI model on exit"); }
    }

    private async Task RunStartupDiagnosticsAsync()
    {
        try
        {
            var diag = new StartupDiagnosticsService(_keyStore, _library);
            await diag.RunAsync();
            await _plugins.InitializeAllAsync();
        }
        catch (Exception ex) { NullActionLogger.Error(nameof(MainViewModel), ex, "Startup diagnostics failed"); }
    }
}