using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using NullWave.Helpers;
using NullWave.Services;
using NullWave.Services.SmartSorting;
using NullWave.ViewModels.Base;
using Serilog;

namespace NullWave.ViewModels;

public enum AIServiceState { Stopped, Starting, Running, Error }

public class SettingsViewModel : ViewModelBase
{
    private readonly KeyStoreService _keyStore;
    private readonly SecureDeleteService _secureDelete;
    private readonly PreferencesService _prefsService;
    private readonly UpdateService _updater;
    private readonly DependencyUpdateService _deps;
    private readonly ExternalAITagService _externalAI = new();
    
    private System.Threading.Timer? _aiHealthTimer;

    private CancellationTokenSource? _debounceCts;
    private const int DebounceMs = 500;

    private void ScheduleSave()
    {
        _debounceCts?.Cancel();
        _debounceCts = new CancellationTokenSource();
        var token = _debounceCts.Token;
        _ = Task.Run(async () =>
        {
            await Task.Delay(DebounceMs, token);
            Log.Verbose("[Settings] Debounced save completed");
        }, token);
    }

    private string _youtubeApiKey = string.Empty;
    private string _spotifyClientId = string.Empty;
    private string _spotifyClientSecret = string.Empty;
    private string _soundCloudClientId = string.Empty;
    private string _lastFmApiKey = string.Empty;
    private string _openWeatherApiKey = string.Empty;

    private string _updateStatus = "Not checked yet";
    private string _ytDlpStatus = string.Empty;
    private string _vlcStatus = string.Empty;
    private string _ffmpegStatus = string.Empty;
    private string _dotNetStatus = string.Empty;
    private bool _isCheckingUpdate;
    private bool _isUpdatingYtDlp;
    private bool _updateAvailable;
    private string _latestVersion = string.Empty;
    private string _releaseUrl = string.Empty;

    private string _thumbnailStatus = string.Empty;

    private string _hardwareInfo = "Not detected yet";
    private bool _isDetectingHardware;
    private bool _isDownloadingModel;
    private double _modelDownloadProgress;
    private string _modelDownloadStatus = string.Empty;
    private string _moodPlaylistStatus = string.Empty;
    private AIServiceState _aiServiceState = AIServiceState.Stopped;

    private string _repairStatus = string.Empty;
    private bool _isRepairing = false;

    private int _currentSectionIndex = 0;
    public int CurrentSectionIndex
    {
        get => _currentSectionIndex;
        set { _currentSectionIndex = value; OnPropertyChanged(); }
    }

    private string _currentSettingsPage = "General";
    public string CurrentSettingsPage
    {
        get => _currentSettingsPage;
        set { _currentSettingsPage = value; OnPropertyChanged(); }
    }

    private string _externalAIStatus = string.Empty;
    public string ExternalAIStatus
    {
        get => _externalAIStatus;
        set { _externalAIStatus = value; OnPropertyChanged(); }
    }

    public string YouTubeApiKey
    {
        get => _youtubeApiKey;
        set
        {
            _youtubeApiKey = value;
            OnPropertyChanged();
            if (!string.IsNullOrWhiteSpace(value)) _keyStore.SaveKey("YouTube", value);
        }
    }

    public string SpotifyClientId
    {
        get => _spotifyClientId;
        set
        {
            _spotifyClientId = value;
            OnPropertyChanged();
            if (!string.IsNullOrWhiteSpace(value)) _keyStore.SaveKey("Spotify:ClientId", value);
        }
    }

    public string SpotifyClientSecret
    {
        get => _spotifyClientSecret;
        set
        {
            _spotifyClientSecret = value;
            OnPropertyChanged();
            if (!string.IsNullOrWhiteSpace(value)) _keyStore.SaveKey("Spotify:ClientSecret", value);
        }
    }

    public string SoundCloudClientId
    {
        get => _soundCloudClientId;
        set
        {
            _soundCloudClientId = value;
            OnPropertyChanged();
            if (!string.IsNullOrWhiteSpace(value)) _keyStore.SaveKey("SoundCloud", value);
        }
    }

    public string LastFmApiKey
    {
        get => _lastFmApiKey;
        set
        {
            _lastFmApiKey = value;
            OnPropertyChanged();
            if (!string.IsNullOrWhiteSpace(value)) _keyStore.SaveKey("LastFm", value);
        }
    }

    public string OpenWeatherApiKey
    {
        get => _openWeatherApiKey;
        set
        {
            _openWeatherApiKey = value;
            OnPropertyChanged();
            if (!string.IsNullOrWhiteSpace(value))
            {
                _keyStore.SaveKey("OpenWeather", value);
                Log.Information("[Settings] OpenWeather key auto-saved ({Length} chars)", value.Length);
            }
        }
    }

    public string AudioQuality
    {
        get => _prefsService.Current.AudioQuality;
        set { _prefsService.Update(p => p.AudioQuality = value); OnPropertyChanged(); ScheduleSave(); }
    }

    public string AudioFormat
    {
        get => _prefsService.Current.AudioFormat;
        set { _prefsService.Update(p => p.AudioFormat = value); OnPropertyChanged(); ScheduleSave(); }
    }

    public string DownloadDirectory
    {
        get => _prefsService.Current.DownloadDirectory;
        set { _prefsService.Update(p => p.DownloadDirectory = value); OnPropertyChanged(); ScheduleSave(); }
    }

    public bool AutoFetchMetadata
    {
        get => _prefsService.Current.AutoFetchMetadata;
        set { _prefsService.Update(p => p.AutoFetchMetadata = value); OnPropertyChanged(); ScheduleSave(); }
    }

    public bool AutoPlayNext
    {
        get => _prefsService.Current.AutoPlayNext;
        set { _prefsService.Update(p => p.AutoPlayNext = value); OnPropertyChanged(); ScheduleSave(); }
    }

    public bool DownloadOnAdd
    {
        get => _prefsService.Current.DownloadOnAdd;
        set { _prefsService.Update(p => p.DownloadOnAdd = value); OnPropertyChanged(); ScheduleSave(); }
    }

    public bool ScrobbleToLastFm
    {
        get => _prefsService.Current.ScrobbleToLastFm;
        set { _prefsService.Update(p => p.ScrobbleToLastFm = value); OnPropertyChanged(); ScheduleSave(); }
    }

    public string AccentColor
    {
        get => _prefsService.Current.AccentColor;
        set { _prefsService.Update(p => p.AccentColor = value); OnPropertyChanged(); ScheduleSave(); }
    }

    public string TrackRowStyle
    {
        get => _prefsService.Current.TrackRowStyle;
        set { _prefsService.Update(p => p.TrackRowStyle = value); OnPropertyChanged(); ScheduleSave(); }
    }

    public string FontScale
    {
        get => _prefsService.Current.FontScale;
        set { _prefsService.Update(p => p.FontScale = value); OnPropertyChanged(); ScheduleSave(); }
    }

    public bool CompactMode
    {
        get => _prefsService.Current.CompactMode;
        set { _prefsService.Update(p => p.CompactMode = value); OnPropertyChanged(); ScheduleSave(); }
    }

    public string SidebarWidth
    {
        get => _prefsService.Current.SidebarWidth;
        set { _prefsService.Update(p => p.SidebarWidth = value); OnPropertyChanged(); ScheduleSave(); }
    }

    public string SelectedModel
    {
        get => _prefsService.Current.SelectedAIModel;
        set { _prefsService.Update(p => p.SelectedAIModel = value); OnPropertyChanged(); ScheduleSave(); }
    }

    public bool UseLocalAI
    {
        get => _prefsService.Current.UseLocalAI;
        set { _prefsService.Update(p => p.UseLocalAI = value); OnPropertyChanged(); ScheduleSave(); }
    }

    public double Latitude
    {
        get => _prefsService.Current.Latitude;
        set { _prefsService.Update(p => p.Latitude = value); OnPropertyChanged(); ScheduleSave(); }
    }

    public double Longitude
    {
        get => _prefsService.Current.Longitude;
        set { _prefsService.Update(p => p.Longitude = value); OnPropertyChanged(); ScheduleSave(); }
    }

    public bool AutoGenerateMoodPlaylist
    {
        get => _prefsService.Current.AutoGenerateMoodPlaylist;
        set { _prefsService.Update(p => p.AutoGenerateMoodPlaylist = value); OnPropertyChanged(); ScheduleSave(); }
    }

    public string MoodRefreshInterval
    {
        get => _prefsService.Current.MoodRefreshInterval;
        set { _prefsService.Update(p => p.MoodRefreshInterval = value); OnPropertyChanged(); ScheduleSave(); }
    }

    public string AIConfidenceThreshold
    {
        get => _prefsService.Current.AIConfidenceThreshold;
        set { _prefsService.Update(p => p.AIConfidenceThreshold = value); OnPropertyChanged(); ScheduleSave(); }
    }

    public string ExportFormat
    {
        get => _prefsService.Current.ExternalAIExportFormat;
        set { _prefsService.Update(p => p.ExternalAIExportFormat = value); OnPropertyChanged(); ScheduleSave(); }
    }

    public bool AIFeaturesEnabled
    {
        get => _prefsService.Current.AIFeaturesEnabled;
        set
        {
            _prefsService.Update(p => p.AIFeaturesEnabled = value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(AIFeaturesControlsEnabled));
            ScheduleSave();
            AIFeaturesEnabledChanged?.Invoke(value);
        }
    }

    public bool AIFeaturesControlsEnabled => AIFeaturesEnabled;

    public bool FadeOnPauseEnabled
    {
        get => _prefsService.Current.FadeOnPauseEnabled;
        set { _prefsService.Update(p => p.FadeOnPauseEnabled = value); OnPropertyChanged(); ScheduleSave(); }
    }

    public int FadeOnPauseDurationMs
    {
        get => _prefsService.Current.FadeOnPauseDurationMs;
        set 
        { 
            _prefsService.Update(p => p.FadeOnPauseDurationMs = value); 
            OnPropertyChanged();
            OnPropertyChanged(nameof(FadeDurationDisplay));
            ScheduleSave();
        }
    }
    public string FadeDurationDisplay => $"{FadeOnPauseDurationMs} ms";

    public bool CrossfadeEnabled
    {
        get => _prefsService.Current.CrossfadeEnabled;
        set { _prefsService.Update(p => p.CrossfadeEnabled = value); OnPropertyChanged(); ScheduleSave(); }
    }

    public int CrossfadeDurationSeconds
    {
        get => _prefsService.Current.CrossfadeDurationSeconds;
        set 
        { 
            _prefsService.Update(p => p.CrossfadeDurationSeconds = value); 
            OnPropertyChanged(); 
            OnPropertyChanged(nameof(CrossfadeDurationDisplay));
            ScheduleSave();
        }
    }
    public string CrossfadeDurationDisplay => $"{CrossfadeDurationSeconds} s";

    public float ScrobbleThreshold
    {
        get => _prefsService.Current.ScrobbleThreshold;
        set
        {
            _prefsService.Update(p => p.ScrobbleThreshold = value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(ScrobbleThresholdDisplay));
            ScheduleSave();
        }
    }

    public int MaxConcurrentDownloads
    {
        get => _prefsService.Current.MaxConcurrentDownloads;
        set
        {
            _prefsService.Update(p => p.MaxConcurrentDownloads = value);
            OnPropertyChanged();
            ScheduleSave();
            MaxConcurrentDownloadsChanged?.Invoke(value);
        }
    }

    public int SkipPenaltyWindowSeconds
    {
        get => _prefsService.Current.SkipPenaltyWindowSeconds;
        set { _prefsService.Update(p => p.SkipPenaltyWindowSeconds = value); OnPropertyChanged(); ScheduleSave(); }
    }

    public int SkipPenaltyCap
    {
        get => _prefsService.Current.SkipPenaltyCap;
        set { _prefsService.Update(p => p.SkipPenaltyCap = value); OnPropertyChanged(); ScheduleSave(); }
    }

    public string ScrobbleThresholdDisplay =>
        $"Scrobble after {ScrobbleThreshold:P0} of track duration";

    public string BatteryModel
    {
        get => _prefsService.Current.BatteryModel;
        set
        {
            _prefsService.Update(p => p.BatteryModel = value);
            OnPropertyChanged();
            ScheduleSave();
            PowerModelsChanged?.Invoke(value, PerformanceModel, AutoPowerModelSwitch);
        }
    }

    public string PerformanceModel
    {
        get => _prefsService.Current.PerformanceModel;
        set
        {
            _prefsService.Update(p => p.PerformanceModel = value);
            OnPropertyChanged();
            ScheduleSave();
            PowerModelsChanged?.Invoke(BatteryModel, value, AutoPowerModelSwitch);
        }
    }

    public bool AutoPowerModelSwitch
    {
        get => _prefsService.Current.AutoPowerModelSwitch;
        set
        {
            _prefsService.Update(p => p.AutoPowerModelSwitch = value);
            OnPropertyChanged();
            ScheduleSave();
            PowerModelsChanged?.Invoke(BatteryModel, PerformanceModel, value);
        }
    }

    private string _powerStateLabel = "Detecting...";
    public string PowerStateLabel
    {
        get => _powerStateLabel;
        set { _powerStateLabel = value; OnPropertyChanged(); }
    }

    public string RepairStatus
    {
        get => _repairStatus;
        set { _repairStatus = value; OnPropertyChanged(); }
    }

    public bool IsRepairing
    {
        get => _isRepairing;
        set { _isRepairing = value; OnPropertyChanged(); }
    }

    public string[] ExportFormatOptions => new[] { "txt", "md", "json" };

    public AIServiceState AIServiceState
    {
        get => _aiServiceState;
        private set
        {
            _aiServiceState = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AIStatusLabel));
            OnPropertyChanged(nameof(AIStatusDescription));
            OnPropertyChanged(nameof(AIStatusDotColor));
            OnPropertyChanged(nameof(AIToggleButtonLabel));
        }
    }

    public string AIStatusLabel => _aiServiceState switch
    {
        AIServiceState.Running  => "Active",
        AIServiceState.Starting => "Starting...",
        AIServiceState.Error    => "Error",
        _                       => "Stopped"
    };

    public string AIStatusDescription => _aiServiceState switch
    {
        AIServiceState.Running  => $"Model '{SelectedModel}' is loaded and ready to process requests.",
        AIServiceState.Starting => "Loading model into Ollama — this may take a moment...",
        AIServiceState.Error    => "Could not reach Ollama. Make sure it is running: ollama serve",
        _                       => "AI service is not running. Click Start to load the selected model."
    };

    public string AIStatusDotColor => _aiServiceState switch
    {
        AIServiceState.Running  => "#4CAF50",
        AIServiceState.Starting => "#FCD34D",
        AIServiceState.Error    => "#F44336",
        _                       => "#6B7280"
    };

    public string AIToggleButtonLabel => _aiServiceState == AIServiceState.Running ? "Stop" : "Start";

    public string HardwareInfo
    {
        get => _hardwareInfo;
        private set { _hardwareInfo = value; OnPropertyChanged(); }
    }

    public bool IsDetectingHardware
    {
        get => _isDetectingHardware;
        private set { _isDetectingHardware = value; OnPropertyChanged(); }
    }

    public bool IsDownloadingModel
    {
        get => _isDownloadingModel;
        private set { _isDownloadingModel = value; OnPropertyChanged(); }
    }

    public double ModelDownloadProgress
    {
        get => _modelDownloadProgress;
        private set { _modelDownloadProgress = value; OnPropertyChanged(); }
    }

    public string ModelDownloadStatus
    {
        get => _modelDownloadStatus;
        private set { _modelDownloadStatus = value; OnPropertyChanged(); }
    }

    public string MoodPlaylistStatus
    {
        get => _moodPlaylistStatus;
        set { _moodPlaylistStatus = value; OnPropertyChanged(); }
    }

    public string ThumbnailStatus
    {
        get => _thumbnailStatus;
        set { _thumbnailStatus = value; OnPropertyChanged(); }
    }

    public string[] AccentColorOptions    => new[] { "Purple", "Blue", "Amber", "Green", "Red" };
    public string[] TrackRowStyleOptions  => new[] { "Comfortable", "Compact", "Cozy" };
    public string[] FontScaleOptions      => new[] { "Small", "Medium", "Large" };
    public string[] SidebarWidthOptions   => new[] { "Narrow", "Normal", "Wide" };
    public string[] AudioQualityOptions   => new[] { "best", "320", "192", "128", "96" };
    public string[] AudioFormatOptions    => new[] { "mp3", "flac", "ogg", "m4a", "wav" };
    
    public string[] AIModelOptions =>
        NullWave.Services.SmartSorting.AIModelCatalog.AllIds;

    public string[] AIModelDisplayOptions =>
        NullWave.Services.SmartSorting.AIModelCatalog.All
            .Select(m => m.OllamaId)
            .ToArray();

    public string[] MoodRefreshOptions    => new[] { "Never", "Every hour", "Every 3 hours", "Daily" };
    public string[] AIConfidenceOptions   => new[] { "50%", "60%", "70%", "80%", "90%" };
    
    public int[] ConcurrentDownloadOptions => new[] { 1, 2, 3, 4, 5 };
    public int[] SkipWindowOptions         => new[] { 5, 10, 15, 20, 30 };
    public int[] SkipPenaltyCapOptions     => new[] { 2, 3, 5, 10 };

    public string UpdateStatus { get => _updateStatus; set { _updateStatus = value; OnPropertyChanged(); } }
    public string YtDlpStatus { get => _ytDlpStatus; set { _ytDlpStatus = value; OnPropertyChanged(); } }
    public string VlcStatus { get => _vlcStatus; set { _vlcStatus = value; OnPropertyChanged(); } }
    public string FfmpegStatus { get => _ffmpegStatus; set { _ffmpegStatus = value; OnPropertyChanged(); } }
    public string DotNetStatus { get => _dotNetStatus; set { _dotNetStatus = value; OnPropertyChanged(); } }
    public bool IsCheckingUpdate { get => _isCheckingUpdate; set { _isCheckingUpdate = value; OnPropertyChanged(); } }
    public bool IsUpdatingYtDlp { get => _isUpdatingYtDlp; set { _isUpdatingYtDlp = value; OnPropertyChanged(); } }
    public bool UpdateAvailable { get => _updateAvailable; set { _updateAvailable = value; OnPropertyChanged(); } }
    public string LatestVersion { get => _latestVersion; set { _latestVersion = value; OnPropertyChanged(); } }
    public string ReleaseUrl { get => _releaseUrl; set { _releaseUrl = value; OnPropertyChanged(); } }
    public string CurrentVersion => _updater.CurrentVersion;

    public ICommand SaveKeysCommand { get; }
    public ICommand DeleteApiKeysCommand { get; }
    public ICommand DeleteLogsCommand { get; }
    public ICommand DeleteEverythingCommand { get; }
    public ICommand BrowseDownloadDirCommand { get; }
    public ICommand CheckForUpdateCommand { get; }
    public ICommand OpenReleasePageCommand { get; }
    public ICommand UpdateYtDlpCommand { get; }
    public ICommand CheckDependenciesCommand { get; }
    public ICommand OpenDataFolderCommand { get; }
    public ICommand OpenLogsFolderCommand { get; }
    public ICommand ClearThumbnailsCommand { get; }

    public ICommand DetectHardwareCommand { get; }
    public ICommand DownloadModelCommand { get; }
    public ICommand GenerateMoodPlaylistCommand { get; }
    public ICommand RefreshWeatherCommand { get; }
    public ICommand ToggleAIServiceCommand { get; }

    public ICommand RepairPathsCommand { get; }
    public ICommand ReimportAssetsCommand { get; }
    public ICommand ForceMetaResyncCommand { get; }

    public ICommand ExportUntaggedTracksCommand { get; private set; } = null!;
    public ICommand ImportAiTagsCommand         { get; private set; } = null!;
    public ICommand NavigateSettingsCommand { get; }

    public event Action? ClearThumbnailsRequested;
    public event Action? GenerateMoodPlaylistRequested;
    public event Action? RefreshWeatherRequested;
    public event Action? ExportUntaggedTracksRequested;
    public event Func<Task<string?>>? ImportAiTagsRequested;
    public event Action<int>? MaxConcurrentDownloadsChanged;
    public event Action<string, string, bool>? PowerModelsChanged;
    public event Action<bool>? AIFeaturesEnabledChanged;
    
    public event Action? RepairPathsRequested;
    public event Action? ReimportAssetsRequested;
    public event Action? ForceMetaResyncRequested;

    public SettingsViewModel(
        KeyStoreService keyStore,
        SecureDeleteService secureDelete,
        PreferencesService prefsService)
    {
        _keyStore = keyStore;
        _secureDelete = secureDelete;
        _prefsService = prefsService;
        _updater = new UpdateService();
        _deps = new DependencyUpdateService();

        _youtubeApiKey     = _keyStore.GetKey("YouTube")           ?? string.Empty;
        _spotifyClientId   = _keyStore.GetKey("Spotify:ClientId")  ?? string.Empty;
        _spotifyClientSecret = _keyStore.GetKey("Spotify:ClientSecret") ?? string.Empty;
        _soundCloudClientId = _keyStore.GetKey("SoundCloud")       ?? string.Empty;
        _lastFmApiKey      = _keyStore.GetKey("LastFm")            ?? string.Empty;
        _openWeatherApiKey = _keyStore.GetKey("OpenWeather")       ?? string.Empty;

        Log.Information("[Settings] API keys loaded — YouTube:{Yt} LastFm:{Fm} OpenWeather:{Ow}",
            _youtubeApiKey.Length, _lastFmApiKey.Length, _openWeatherApiKey.Length);

        RefreshWeatherCommand        = new RelayCommand(() => RefreshWeatherRequested?.Invoke());
        SaveKeysCommand              = new RelayCommand(SaveKeys);
        DeleteApiKeysCommand         = new RelayCommand(DeleteApiKeys);
        DeleteLogsCommand            = new RelayCommand(DeleteLogs);
        DeleteEverythingCommand      = new RelayCommand(DeleteEverything);
        BrowseDownloadDirCommand     = new RelayCommand(async () => await BrowseDownloadDirAsync());
        CheckForUpdateCommand        = new RelayCommand(async () => await CheckForUpdateAsync());
        UpdateYtDlpCommand           = new RelayCommand(async () => await UpdateYtDlpAsync());
        CheckDependenciesCommand     = new RelayCommand(async () => await CheckDependenciesAsync());
        OpenDataFolderCommand        = new RelayCommand(() => OpenFolder(NullWavePaths.DataDir));
        OpenLogsFolderCommand        = new RelayCommand(() => OpenFolder(NullWavePaths.LogsDir));
        OpenReleasePageCommand       = new RelayCommand(() =>
        {
            if (!string.IsNullOrEmpty(_releaseUrl))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    { FileName = _releaseUrl, UseShellExecute = true });
        });

        ClearThumbnailsCommand = new RelayCommand(() =>
        {
            ThumbnailStatus = "Clearing thumbnails...";
            ClearThumbnailsRequested?.Invoke();
        });

        RepairPathsCommand = new RelayCommand(() =>
        {
            IsRepairing  = true;
            RepairStatus = "Scanning file paths...";
            RepairPathsRequested?.Invoke();
        });

        ReimportAssetsCommand = new RelayCommand(() =>
        {
            IsRepairing  = true;
            RepairStatus = "Scanning download folder...";
            ReimportAssetsRequested?.Invoke();
        });

        ForceMetaResyncCommand = new RelayCommand(() =>
        {
            IsRepairing  = true;
            RepairStatus = "Clearing cached tags — re-sync starting...";
            ForceMetaResyncRequested?.Invoke();
        });

        DetectHardwareCommand        = new RelayCommand(DetectHardware);
        DownloadModelCommand         = new RelayCommand(async () => await DownloadModelAsync());
        GenerateMoodPlaylistCommand  = new RelayCommand(() =>
        {
            MoodPlaylistStatus = "Generating mood playlist...";
            GenerateMoodPlaylistRequested?.Invoke();
        });
        ToggleAIServiceCommand       = new RelayCommand(async () => await ToggleAIServiceAsync());

        ExportUntaggedTracksCommand = new RelayCommand(async () => await ExportUntaggedTracksAsync());
        ImportAiTagsCommand         = new RelayCommand(async () => await ImportAiTagsAsync());

        NavigateSettingsCommand = new RelayCommand<string>(page =>
        {
            if (!string.IsNullOrWhiteSpace(page))
                CurrentSettingsPage = page;
        });

        DetectHardware();

        _ = ProbeOllamaOnStartupAsync();
        StartAIHealthCheck();
    }

    private async Task ToggleAIServiceAsync()
    {
        if (!AIFeaturesEnabled)
        {
            AIServiceState = AIServiceState.Stopped;
            return;
        }

        if (_aiServiceState == AIServiceState.Running)
        {
            AIServiceState = AIServiceState.Stopped;
            Log.Information("[Settings] AI service stopped by user");
            return;
        }

        AIServiceState = AIServiceState.Starting;
        try
        {
            var ai = new LocalAIService();
            ai.CurrentModel = SelectedModel;
            bool ok = await ai.PingAsync();
            AIServiceState = ok ? AIServiceState.Running : AIServiceState.Error;
            Log.Information("[Settings] AI service ping: {Ok}", ok);
        }
        catch (Exception ex)
        {
            AIServiceState = AIServiceState.Error;
            Log.Error(ex, "[Settings] AI service start failed");
        }
    }

    private async Task ProbeOllamaOnStartupAsync()
    {
        try
        {
            if (!AIFeaturesEnabled)
            {
                AIServiceState = AIServiceState.Stopped;
                Log.Information("[Settings] AI features disabled — skipping Ollama probe");
                return;
            }

            var ai = new LocalAIService();
            bool running = await ai.PingAsync();
            if (_aiServiceState == AIServiceState.Stopped)
                AIServiceState = running ? AIServiceState.Running : AIServiceState.Stopped;

            Log.Information("[Settings] Startup Ollama probe: {Running}", running);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[Settings] Startup Ollama probe failed");
        }
    }

    public void StartAIHealthCheck()
    {
        _aiHealthTimer = new System.Threading.Timer(
            async _ => await HealthCheckTickAsync(),
            null,
            dueTime:  TimeSpan.FromSeconds(30),
            period:   TimeSpan.FromSeconds(30));
    }

    private async Task HealthCheckTickAsync()
    {
        try
        {
            if (!AIFeaturesEnabled || _aiServiceState == AIServiceState.Stopped) return;

            var ai = new LocalAIService();
            bool reachable = await ai.PingAsync();

            var newState = reachable ? AIServiceState.Running : AIServiceState.Error;

            if (_aiServiceState != newState)
            {
                AIServiceState = newState;
                Log.Information("[Settings] AI health check: state changed to {State}", newState);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[Settings] AI health check tick failed");
        }
    }

    public void StopHealthCheck()
    {
        _aiHealthTimer?.Dispose();
        _aiHealthTimer = null;
    }

    public void SetAIServiceState(AIServiceState state)
    {
        AIServiceState = state;
    }

    public void ReportThumbnailsCleared(int count)
    {
        ThumbnailStatus = $"Cleared {count} thumbnails — re-fetching in background...";
        Log.Information("[Settings] Thumbnails cleared: {Count}", count);
    }

    public void ReportMoodPlaylistGenerated(int trackCount, string mood)
    {
        MoodPlaylistStatus = $"Generated {trackCount} tracks for mood: {mood}";
        Log.Information("[Settings] Mood playlist generated: {Count} tracks, mood: {Mood}", trackCount, mood);
    }

    public void ReportMoodPlaylistFailed(string reason)
    {
        MoodPlaylistStatus = $"Failed: {reason}";
        Log.Warning("[Settings] Mood playlist generation failed: {Reason}", reason);
    }

    public void ReportRepairPathsComplete(int total, int missing, int cleared)
    {
        IsRepairing  = false;
        RepairStatus = missing == 0
            ? $"✓ All {total} file paths are valid."
            : $"Found {missing} missing file(s) — {cleared} path(s) cleared for re-download.";
        NullWave.Services.ToastService.Instance.Show(RepairStatus,
            missing == 0 ? NullWave.Models.ToastType.Success : NullWave.Models.ToastType.Warning);
        Log.Information("[Settings] RepairPaths: {Total} checked, {Missing} missing, {Cleared} cleared",
            total, missing, cleared);
    }

    public void ReportReimportComplete(int relinked)
    {
        IsRepairing  = false;
        RepairStatus = relinked == 0
            ? "No new file matches found in download folder."
            : $"✓ Re-linked {relinked} track(s) to audio files on disk.";
        NullWave.Services.ToastService.Instance.Show(RepairStatus,
            relinked > 0 ? NullWave.Models.ToastType.Success : NullWave.Models.ToastType.Info);
        Log.Information("[Settings] Reimport: {Count} tracks re-linked", relinked);
    }

    public void ReportMetaResyncComplete(int cleared)
    {
        IsRepairing  = false;
        RepairStatus = $"✓ Cleared tags for {cleared} track(s) — Last.fm re-sync running in background.";
        NullWave.Services.ToastService.Instance.Show(RepairStatus, NullWave.Models.ToastType.Success);
        Log.Information("[Settings] MetaResync: {Count} tracks cleared", cleared);
    }

    public void ReportRepairFailed(string operation, string reason)
    {
        IsRepairing  = false;
        RepairStatus = $"✗ {operation} failed: {reason}";
        NullWave.Services.ToastService.Instance.Show(RepairStatus, NullWave.Models.ToastType.Error);
        Log.Error("[Settings] Repair failed — {Op}: {Reason}", operation, reason);
    }

    private Task ExportUntaggedTracksAsync()
    {
        ExternalAIStatus = "Preparing export...";
        ExportUntaggedTracksRequested?.Invoke();
        return Task.CompletedTask;
    }

    public async Task ReportExportReadyAsync(
        IEnumerable<NullWave.Models.Track> tracks,
        Avalonia.Controls.Window parentWindow)
    {
        var trackList = tracks.ToList();

        if (trackList.Count == 0)
        {
            ExternalAIStatus = "No untagged tracks found — nothing to export.";
            NullWave.Services.ToastService.Instance.Show(
                "All tracks already have tags.", NullWave.Models.ToastType.Info);
            return;
        }

        var format = ExportFormat ?? "txt";
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmm");
        var baseFileName = $"nullwave_ai_prompt_{timestamp}.{format}";

        var chunks = _externalAI.GenerateChunked(trackList, format, baseFileName);

        var sp = new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title             = chunks.Count > 1
                ? $"Save AI Prompt — Part 1 of {chunks.Count} (you'll save each in turn)"
                : "Save AI Tagging Prompt",
            SuggestedFileName = chunks[0].FileName,
            FileTypeChoices   = new[]
            {
                new Avalonia.Platform.Storage.FilePickerFileType("Text") { Patterns = new[] { "*.txt" } },
                new Avalonia.Platform.Storage.FilePickerFileType("Markdown") { Patterns = new[] { "*.md" } },
                new Avalonia.Platform.Storage.FilePickerFileType("JSON") { Patterns = new[] { "*.json" } },
            }
        };

        int savedCount = 0;
        foreach (var (content, fileName) in chunks)
        {
            sp.SuggestedFileName = fileName;
            if (savedCount > 0)
                sp.Title = $"Save AI Prompt — Part {savedCount + 1} of {chunks.Count}";

            var file = await parentWindow.StorageProvider.SaveFilePickerAsync(sp);
            if (file == null)
            {
                ExternalAIStatus = savedCount == 0
                    ? "Export cancelled."
                    : $"Partial export — saved {savedCount} of {chunks.Count} files.";
                return;
            }

            try
            {
                await using var stream = await file.OpenWriteAsync();
                await using var writer = new System.IO.StreamWriter(stream);
                await writer.WriteAsync(content);
                savedCount++;
                Log.Information("[Settings] Exported chunk {N}/{Total}: {File}",
                    savedCount, chunks.Count, file.Name);
            }
            catch (Exception ex)
            {
                ExternalAIStatus = $"Export failed on part {savedCount + 1}: {ex.Message}";
                NullWave.Services.ToastService.Instance.Show(
                    "Export failed — check logs.", NullWave.Models.ToastType.Error);
                Log.Error(ex, "[Settings] Export chunk {N} failed", savedCount + 1);
                return;
            }
        }

        var summary = chunks.Count > 1
            ? $"Exported {trackList.Count} tracks in {chunks.Count} files"
            : $"Exported {trackList.Count} tracks → {chunks[0].FileName}";

        ExternalAIStatus = summary;
        NullWave.Services.ToastService.Instance.Show(summary, NullWave.Models.ToastType.Success);
    }

    private async Task ImportAiTagsAsync()
    {
        var task = ImportAiTagsRequested?.Invoke();
        if (task != null) await task;
    }

    public void ReportImportComplete(int applied, int total)
    {
        if (applied == 0 && total == 0)
        {
            ExternalAIStatus = "Import cancelled or file was empty.";
            return;
        }

        ExternalAIStatus = total == 0
            ? "No matching tracks found in import."
            : $"Import complete — tagged {applied} of {total} tracks.";

        var type = applied > 0 ? NullWave.Models.ToastType.Success : NullWave.Models.ToastType.Warning;
        NullWave.Services.ToastService.Instance.Show(ExternalAIStatus, type);

        Log.Information("[Settings] External AI import: {Applied}/{Total} tracks tagged", applied, total);
    }

    public void ReportImportFailed(string reason)
    {
        ExternalAIStatus = $"Import failed: {reason}";
        NullWave.Services.ToastService.Instance.Show(
            $"Import failed: {reason}", NullWave.Models.ToastType.Error);
        Log.Error("[Settings] External AI import failed: {Reason}", reason);
    }

    private void DetectHardware()
    {
        IsDetectingHardware = true;
        try
        {
            var detector = new HardwareDetector();
            var info = detector.Detect();
            HardwareInfo = $"CPU: {info.CpuCores} cores | RAM: {info.RamGB}GB\n" +
                           $"GPU: {info.GpuType} ({info.GpuVramGB}GB VRAM)\n" +
                           $"Recommended: {info.RecommendedModel}\n" +
                           $"{info.RecommendationReason}";
            SelectedModel = info.RecommendedModel;

            var suggestedBattery = NullWave.Services.SmartSorting.AIModelCatalog
                .SuggestBatteryModel(info.RamGB);
            var suggestedPerf = NullWave.Services.SmartSorting.AIModelCatalog
                .SuggestPerformanceModel(info.RamGB, info.GpuVramGB, info.HasNvidia || info.HasAmd);

            if (BatteryModel == "qwen2.5:3b" || string.IsNullOrEmpty(BatteryModel))
                BatteryModel = suggestedBattery;
            if (PerformanceModel == "qwen2.5:7b" || string.IsNullOrEmpty(PerformanceModel))
                PerformanceModel = suggestedPerf;

            Log.Information("[Settings] Hardware detected: {Info}", HardwareInfo);
        }
        catch (Exception ex)
        {
            HardwareInfo = $"Detection failed: {ex.Message}";
            Log.Error(ex, "[Settings] Hardware detection failed");
        }
        finally
        {
            IsDetectingHardware = false;
        }
    }

    private async Task DownloadModelAsync()
    {
        if (IsDownloadingModel) return;
        IsDownloadingModel = true;
        ModelDownloadProgress = 0;
        ModelDownloadStatus = $"Downloading {SelectedModel}...";
        try
        {
            var ai = new LocalAIService();
            var progress = new Progress<double>(pct =>
            {
                ModelDownloadProgress = pct * 100;
                ModelDownloadStatus = $"Downloading {SelectedModel}... {pct:P0}";
            });
            await ai.DownloadModelAsync(SelectedModel, progress);
            ModelDownloadStatus = $"✓ {SelectedModel} downloaded successfully";
            await ToggleAIServiceAsync();
            Log.Information("[Settings] Model downloaded: {Model}", SelectedModel);
        }
        catch (Exception ex)
        {
            ModelDownloadStatus = $"✗ Download failed: {ex.Message}";
            Log.Error(ex, "[Settings] Model download failed");
        }
        finally
        {
            IsDownloadingModel = false;
        }
    }

    private void SaveKeys()
    {
        if (!string.IsNullOrWhiteSpace(YouTubeApiKey))       _keyStore.SaveKey("YouTube", YouTubeApiKey);
        if (!string.IsNullOrWhiteSpace(SpotifyClientId))     _keyStore.SaveKey("Spotify:ClientId", SpotifyClientId);
        if (!string.IsNullOrWhiteSpace(SpotifyClientSecret)) _keyStore.SaveKey("Spotify:ClientSecret", SpotifyClientSecret);
        if (!string.IsNullOrWhiteSpace(SoundCloudClientId))  _keyStore.SaveKey("SoundCloud", SoundCloudClientId);
        if (!string.IsNullOrWhiteSpace(LastFmApiKey))         _keyStore.SaveKey("LastFm", LastFmApiKey);
        if (!string.IsNullOrWhiteSpace(OpenWeatherApiKey))   _keyStore.SaveKey("OpenWeather", OpenWeatherApiKey);
        Log.Information("[Settings] API keys saved (manual)");
    }

    private void DeleteApiKeys()
    {
        _secureDelete.DeleteApiKeys();
        YouTubeApiKey = SpotifyClientId = SpotifyClientSecret =
            SoundCloudClientId = LastFmApiKey = OpenWeatherApiKey = string.Empty;
        Log.Warning("[Settings] All API keys deleted");
    }

    private void DeleteLogs()
    {
        _secureDelete.DeleteLogs();
        Log.Warning("[Settings] Logs deleted");
    }

    private void DeleteEverything()
    {
        _secureDelete.DeleteEverything();
        YouTubeApiKey = SpotifyClientId = SpotifyClientSecret =
            SoundCloudClientId = LastFmApiKey = OpenWeatherApiKey = string.Empty;
        Log.Warning("[Settings] Full data wipe performed");
    }

    private static void OpenFolder(string path)
    {
        System.IO.Directory.CreateDirectory(path);
        System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo { FileName = path, UseShellExecute = true });
    }

    private async Task BrowseDownloadDirAsync()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
            desktop.MainWindow == null) return;

        var folders = await desktop.MainWindow.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = "Select Download Directory" });

        if (folders.Count > 0)
            DownloadDirectory = folders[0].Path.LocalPath;
    }

    private async Task CheckForUpdateAsync()
    {
        IsCheckingUpdate = true;
        UpdateStatus = "Checking...";
        try
        {
            var result = await _updater.CheckForUpdateAsync();
            UpdateAvailable = result.IsUpdateAvailable;
            LatestVersion   = result.LatestVersion;
            ReleaseUrl      = result.ReleaseUrl;
            UpdateStatus = result.IsUpdateAvailable
                ? $"Update available: v{result.LatestVersion} (published {result.PublishedAt:dd-MMM-yyyy})"
                : result.LatestVersion == "unknown"
                    ? "Could not reach GitHub — check your connection"
                    : $"You are up to date (v{result.CurrentVersion})";
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }

    private async Task UpdateYtDlpAsync()
    {
        IsUpdatingYtDlp = true;
        YtDlpStatus = "Updating yt-dlp...";
        try
        {
            var result = await _deps.UpdateYtDlpAsync();
            YtDlpStatus = result;
            var info = await _deps.GetYtDlpInfoAsync();
            YtDlpStatus = info.IsInstalled
                ? $"yt-dlp {info.InstalledVersion} (up to date)"
                : "yt-dlp not found";
        }
        finally
        {
            IsUpdatingYtDlp = false;
        }
    }

    private async Task CheckDependenciesAsync()
    {
        YtDlpStatus = VlcStatus = FfmpegStatus = DotNetStatus = "Checking...";
        var ytDlp  = await _deps.GetYtDlpInfoAsync();
        var vlc    = await _deps.GetVlcInfoAsync();
        var ffmpeg = await _deps.GetFfmpegInfoAsync();
        var dotNet = await _deps.GetDotNetInfoAsync();
        YtDlpStatus  = ytDlp.IsInstalled  ? ytDlp.InstalledVersion  : "Not installed";
        VlcStatus    = vlc.IsInstalled     ? vlc.InstalledVersion    : "Not installed";
        FfmpegStatus = ffmpeg.IsInstalled  ? ffmpeg.InstalledVersion : "Not installed";
        DotNetStatus = dotNet.IsInstalled  ? dotNet.InstalledVersion : "Not found";
    }
}