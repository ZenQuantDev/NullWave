using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NullWave.Helpers;
using NullWave.Helpers.Logging;
using NullWave.Models;
using NullWave.Services;
using NullWave.Services.Plugins;
using NullWave.Services.SmartSorting;
using NullWave.ViewModels.Settings;
using Serilog;
using Serilog.Events;

namespace NullWave.ViewModels;

public enum AIServiceState { Stopped, Starting, Running, Error }
public enum LastFmConnectionState { Disconnected, AwaitingAuth, Connected, Error }

public partial class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly KeyStoreService _keyStore;
    private readonly SecureDeleteService _secureDelete;
    private readonly PreferencesService _prefsService;
    private readonly UpdateService _updater;
    private readonly DependencyUpdateService _deps;
    private readonly ExternalAITagService _externalAI = new();
    private readonly LocalAIService _localAI;
    private readonly PluginManager _plugins;

    private System.Threading.Timer? _aiHealthTimer;
    private CancellationTokenSource? _debounceCts;
    private const int DebounceMs = 500;

    public System.Collections.ObjectModel.ObservableCollection<NullWave.Models.LiveNotification> ActiveToasts => NullWave.Services.ToastService.Instance.ActiveToasts;

    public ObservableCollection<PluginRowViewModel> PluginRows { get; }

    private void ScheduleSave()
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = new CancellationTokenSource();
        var token = _debounceCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(DebounceMs, token);
                if (!token.IsCancellationRequested)
                {
                    _prefsService.Save();
                    Log.Information("[Settings] Debounced save successfully written to disk.");
                }
            }
            catch (OperationCanceledException)
            {
                Log.Verbose("[Settings] Previous save execution shifted; token canceled.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Settings] Critical error trying to save application configurations.");
            }
        }, token);
    }

    // API Keys
    private string _youtubeApiKey = string.Empty;
    public string YouTubeApiKey
    {
        get => _youtubeApiKey;
        set { if (SetProperty(ref _youtubeApiKey, value)) { } }
    }

    private string _spotifyClientId = string.Empty;
    public string SpotifyClientId
    {
        get => _spotifyClientId;
        set { if (SetProperty(ref _spotifyClientId, value)) { } }
    }

    private string _spotifyClientSecret = string.Empty;
    public string SpotifyClientSecret
    {
        get => _spotifyClientSecret;
        set { if (SetProperty(ref _spotifyClientSecret, value)) { } }
    }

    private string _soundCloudClientId = string.Empty;
    public string SoundCloudClientId
    {
        get => _soundCloudClientId;
        set { if (SetProperty(ref _soundCloudClientId, value)) { } }
    }

    private string _lastFmApiKey = string.Empty;
    public string LastFmApiKey
    {
        get => _lastFmApiKey;
        set { if (SetProperty(ref _lastFmApiKey, value)) { } }
    }

    private string _lastFmApiSecret = string.Empty;
    public string LastFmApiSecret
    {
        get => _lastFmApiSecret;
        set { if (SetProperty(ref _lastFmApiSecret, value)) { } }
    }

    private string _openWeatherApiKey = string.Empty;
    public string OpenWeatherApiKey
    {
        get => _openWeatherApiKey;
        set { if (SetProperty(ref _openWeatherApiKey, value)) { } }
    }

    // Preferences pass-through properties
    public string AudioQuality { get => _prefsService.Current.AudioQuality; set { _prefsService.Update(p => p.AudioQuality = value); OnPropertyChanged(); ScheduleSave(); } }
    public string AudioFormat { get => _prefsService.Current.AudioFormat; set { _prefsService.Update(p => p.AudioFormat = value); OnPropertyChanged(); ScheduleSave(); } }
    public string DownloadDirectory { get => _prefsService.Current.DownloadDirectory; set { _prefsService.Update(p => p.DownloadDirectory = value); OnPropertyChanged(); ScheduleSave(); } }
    public bool AutoFetchMetadata { get => _prefsService.Current.AutoFetchMetadata; set { _prefsService.Update(p => p.AutoFetchMetadata = value); OnPropertyChanged(); ScheduleSave(); } }
    public bool AutoPlayNext { get => _prefsService.Current.AutoPlayNext; set { _prefsService.Update(p => p.AutoPlayNext = value); OnPropertyChanged(); ScheduleSave(); } }
    public bool DownloadOnAdd { get => _prefsService.Current.DownloadOnAdd; set { _prefsService.Update(p => p.DownloadOnAdd = value); OnPropertyChanged(); ScheduleSave(); } }
    public bool ScrobbleToLastFm { get => _prefsService.Current.ScrobbleToLastFm; set { _prefsService.Update(p => p.ScrobbleToLastFm = value); OnPropertyChanged(); ScheduleSave(); } }
    public bool AutoCleanMetadata { get => _prefsService.Current.AutoCleanMetadata; set { _prefsService.Update(p => p.AutoCleanMetadata = value); OnPropertyChanged(); ScheduleSave(); } }
    public bool PreventDuplicateDownloads { get => _prefsService.Current.PreventDuplicateDownloads; set { _prefsService.Update(p => p.PreventDuplicateDownloads = value); OnPropertyChanged(); ScheduleSave(); } }
    public bool UseAria2c { get => _prefsService.Current.UseAria2c; set { _prefsService.Update(p => p.UseAria2c = value); OnPropertyChanged(); ScheduleSave(); } }
    public string YtDlpBrowserCookies { get => _prefsService.Current.YtDlpBrowserCookies; set { _prefsService.Update(p => p.YtDlpBrowserCookies = value); OnPropertyChanged(); ScheduleSave(); } }
    public string[] BrowserCookieOptions => new[] { "", "firefox", "chrome", "chromium", "brave", "vivaldi", "edge" };

    public bool VerboseLogging
    {
        get => _prefsService.Current.VerboseLogging;
        set
        {
            _prefsService.Update(p => p.VerboseLogging = value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(LoggingModeLabel));
            ScheduleSave();
            NullWaveLogConfig.LevelSwitch.MinimumLevel = value ? LogEventLevel.Debug : LogEventLevel.Information;
            Log.Information("[Settings] Logging mode changed to {Mode}", value ? "Advanced/Verbose" : "Default");
        }
    }

    public string LoggingModeLabel => VerboseLogging ? "Advanced / Verbose" : "Default";

    public string AccentColor
    {
        get => _prefsService.Current.AccentColor;
        set
        {
            _prefsService.Update(p => p.AccentColor = value);
            OnPropertyChanged();
            ThemeService.Instance.ApplyAccent(value);
            ScheduleSave();
        }
    }

    public string TrackRowStyle
    {
        get => _prefsService.Current.TrackRowStyle;
        set
        {
            _prefsService.Update(p => p.TrackRowStyle = value);
            OnPropertyChanged();
            ThemeService.Instance.ApplyDensity(_prefsService.Current);
            ScheduleSave();
        }
    }

    public string FontScale
    {
        get => _prefsService.Current.FontScale;
        set
        {
            _prefsService.Update(p => p.FontScale = value);
            OnPropertyChanged();
            ThemeService.Instance.ApplyFontScale(value);
            ScheduleSave();
        }
    }

    public bool CompactMode
    {
        get => _prefsService.Current.CompactMode;
        set
        {
            _prefsService.Update(p => p.CompactMode = value);
            OnPropertyChanged();
            ThemeService.Instance.ApplyDensity(_prefsService.Current);
            ScheduleSave();
        }
    }

    public string SidebarWidth
    {
        get => _prefsService.Current.SidebarWidth;
        set
        {
            _prefsService.Update(p => p.SidebarWidth = value);
            OnPropertyChanged();
            ThemeService.Instance.ApplySidebarWidth(value);
            ScheduleSave();
        }
    }

    [RelayCommand] private void SetAccent(string name) => AccentColor = name;
    [RelayCommand] private void SetRowStyle(string style) => TrackRowStyle = style;
    [RelayCommand] private void SetFontScale(string v) => FontScale = v;
    [RelayCommand] private void SetSidebarWidth(string v) => SidebarWidth = v;

    public string SelectedModel { get => _prefsService.Current.SelectedAIModel; set { _prefsService.Update(p => p.SelectedAIModel = value); OnPropertyChanged(); ScheduleSave(); } }
    public bool UseLocalAI { get => _prefsService.Current.UseLocalAI; set { _prefsService.Update(p => p.UseLocalAI = value); OnPropertyChanged(); ScheduleSave(); } }
    public double Latitude { get => _prefsService.Current.Latitude; set { _prefsService.Update(p => p.Latitude = value); OnPropertyChanged(); ScheduleSave(); } }
    public double Longitude { get => _prefsService.Current.Longitude; set { _prefsService.Update(p => p.Longitude = value); OnPropertyChanged(); ScheduleSave(); } }
    public bool AutoGenerateMoodPlaylist { get => _prefsService.Current.AutoGenerateMoodPlaylist; set { _prefsService.Update(p => p.AutoGenerateMoodPlaylist = value); OnPropertyChanged(); ScheduleSave(); } }
    public string MoodRefreshInterval { get => _prefsService.Current.MoodRefreshInterval; set { _prefsService.Update(p => p.MoodRefreshInterval = value); OnPropertyChanged(); ScheduleSave(); } }
    public string AIConfidenceThreshold { get => _prefsService.Current.AIConfidenceThreshold; set { _prefsService.Update(p => p.AIConfidenceThreshold = value); OnPropertyChanged(); ScheduleSave(); } }
    public string ExportFormat { get => _prefsService.Current.ExternalAIExportFormat; set { _prefsService.Update(p => p.ExternalAIExportFormat = value); OnPropertyChanged(); ScheduleSave(); } }

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

    public bool FadeOnPauseEnabled { get => _prefsService.Current.FadeOnPauseEnabled; set { _prefsService.Update(p => p.FadeOnPauseEnabled = value); OnPropertyChanged(); ScheduleSave(); } }
    public int FadeOnPauseDurationMs
    {
        get => _prefsService.Current.FadeOnPauseDurationMs;
        set { _prefsService.Update(p => p.FadeOnPauseDurationMs = value); OnPropertyChanged(); OnPropertyChanged(nameof(FadeDurationDisplay)); ScheduleSave(); }
    }
    public bool CrossfadeEnabled { get => _prefsService.Current.CrossfadeEnabled; set { _prefsService.Update(p => p.CrossfadeEnabled = value); OnPropertyChanged(); ScheduleSave(); } }
    public int CrossfadeDurationSeconds
    {
        get => _prefsService.Current.CrossfadeDurationSeconds;
        set { _prefsService.Update(p => p.CrossfadeDurationSeconds = value); OnPropertyChanged(); OnPropertyChanged(nameof(CrossfadeDurationDisplay)); ScheduleSave(); }
    }
    public float ScrobbleThreshold
    {
        get => _prefsService.Current.ScrobbleThreshold;
        set { _prefsService.Update(p => p.ScrobbleThreshold = value); OnPropertyChanged(); OnPropertyChanged(nameof(ScrobbleThresholdDisplay)); ScheduleSave(); }
    }
    public int MaxConcurrentDownloads
    {
        get => _prefsService.Current.MaxConcurrentDownloads;
        set { _prefsService.Update(p => p.MaxConcurrentDownloads = value); OnPropertyChanged(); ScheduleSave(); MaxConcurrentDownloadsChanged?.Invoke(value); }
    }
    public int SkipPenaltyWindowSeconds { get => _prefsService.Current.SkipPenaltyWindowSeconds; set { _prefsService.Update(p => p.SkipPenaltyWindowSeconds = value); OnPropertyChanged(); ScheduleSave(); } }
    public int SkipPenaltyCap { get => _prefsService.Current.SkipPenaltyCap; set { _prefsService.Update(p => p.SkipPenaltyCap = value); OnPropertyChanged(); ScheduleSave(); } }
    public string BatteryModel
    {
        get => _prefsService.Current.BatteryModel;
        set { _prefsService.Update(p => p.BatteryModel = value); OnPropertyChanged(); ScheduleSave(); PowerModelsChanged?.Invoke(value, PerformanceModel, AutoPowerModelSwitch); }
    }
    public string PerformanceModel
    {
        get => _prefsService.Current.PerformanceModel;
        set { _prefsService.Update(p => p.PerformanceModel = value); OnPropertyChanged(); ScheduleSave(); PowerModelsChanged?.Invoke(BatteryModel, value, AutoPowerModelSwitch); }
    }
    public bool AutoPowerModelSwitch
    {
        get => _prefsService.Current.AutoPowerModelSwitch;
        set { _prefsService.Update(p => p.AutoPowerModelSwitch = value); OnPropertyChanged(); ScheduleSave(); PowerModelsChanged?.Invoke(BatteryModel, PerformanceModel, value); }
    }

    // Queue Auto-Fill Settings
    public int QueueAutoFillSize
    {
        get => _prefsService.Current.QueueAutoFillSize;
        set { _prefsService.Update(p => p.QueueAutoFillSize = value); OnPropertyChanged(); ScheduleSave(); }
    }

    public bool QueueManualInsertAtBlockEnd
    {
        get => _prefsService.Current.QueueManualInsertAtBlockEnd;
        set { _prefsService.Update(p => p.QueueManualInsertAtBlockEnd = value); OnPropertyChanged(); ScheduleSave(); }
    }

    [ObservableProperty] private int _currentSectionIndex = 0;
    [ObservableProperty] private string _currentSettingsPage = "General";
    [ObservableProperty] private string _externalAIStatus = string.Empty;
    [ObservableProperty] private string _repairStatus = string.Empty;
    [ObservableProperty] private string _sweepStatus = string.Empty;
    [ObservableProperty] private string _vacuumStatus = string.Empty;
    [ObservableProperty] private string _verifyLinksStatus = string.Empty;
    [ObservableProperty] private string _forceCleanStatus = string.Empty;
    [ObservableProperty] private string _dedupeStatus = string.Empty;
    [ObservableProperty] private bool _isRepairing = false;
    [ObservableProperty] private string _updateStatus = "Not checked yet";
    [ObservableProperty] private string _ytDlpStatus = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstallVlc))]
    private string _vlcStatus = string.Empty;

    [ObservableProperty] private string _ffmpegStatus = string.Empty;
    [ObservableProperty] private string _dotNetStatus = string.Empty;
    [ObservableProperty] private bool _isCheckingUpdate;
    [ObservableProperty] private bool _isUpdatingYtDlp;
    [ObservableProperty] private bool _updateAvailable;
    [ObservableProperty] private string _latestVersion = string.Empty;
    [ObservableProperty] private string _releaseUrl = string.Empty;
    [ObservableProperty] private string _thumbnailStatus = string.Empty;
    [ObservableProperty] private string _hardwareInfo = "Not detected yet";
    [ObservableProperty] private bool _isDetectingHardware;
    [ObservableProperty] private bool _isDownloadingModel;
    [ObservableProperty] private double _modelDownloadProgress;
    [ObservableProperty] private string _modelDownloadStatus = string.Empty;
    [ObservableProperty] private string _moodPlaylistStatus = string.Empty;
    [ObservableProperty] private string _powerStateLabel = "Detecting...";
    [ObservableProperty] private bool _isStagingUpdate;
    [ObservableProperty] private bool _updateStaged;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AIStatusLabel))]
    [NotifyPropertyChangedFor(nameof(AIStatusDescription))]
    [NotifyPropertyChangedFor(nameof(AIStatusDotColor))]
    [NotifyPropertyChangedFor(nameof(AIToggleButtonLabel))]
    private AIServiceState _aiServiceState = AIServiceState.Stopped;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LastFmStateLabel))]
    [NotifyPropertyChangedFor(nameof(IsLastFmConnected))]
    [NotifyPropertyChangedFor(nameof(IsLastFmAwaitingAuth))]
    private LastFmConnectionState _lastFmState = LastFmConnectionState.Disconnected;

    [ObservableProperty] private string _lastFmUsername = string.Empty;
    [ObservableProperty] private string _lastFmStatusMessage = string.Empty;

    public bool IsLastFmConnected     => LastFmState == LastFmConnectionState.Connected;
    public bool IsLastFmAwaitingAuth  => LastFmState == LastFmConnectionState.AwaitingAuth;
    public string LastFmStateLabel => LastFmState switch
    {
        LastFmConnectionState.Connected     => $"Connected as {LastFmUsername}",
        LastFmConnectionState.AwaitingAuth  => "Waiting for authorization in browser...",
        LastFmConnectionState.Error         => "Connection failed",
        _                                   => "Not connected"
    };

    public string AIStatusLabel => AiServiceState switch { AIServiceState.Running => "Active", AIServiceState.Starting => "Starting...", AIServiceState.Error => "Error", _ => "Stopped" };
    public string AIStatusDescription => AiServiceState switch { AIServiceState.Running => $"Model '{SelectedModel}' is loaded and ready.", AIServiceState.Starting => "Loading model...", AIServiceState.Error => "Could not reach Ollama.", _ => "AI service is not running." };
    public string AIStatusDotColor => AiServiceState switch { AIServiceState.Running => "#4CAF50", AIServiceState.Starting => "#FCD34D", AIServiceState.Error => "#F44336", _ => "#6B7280" };
    public string AIToggleButtonLabel => AiServiceState == AIServiceState.Running ? "Stop" : "Start";

    public string[] ExportFormatOptions => new[] { "txt", "md", "json" };
    public string[] AccentColorOptions => new[] { "Blue Orchid", "Purple", "Sky", "Green", "Amber", "Red", "Pink", "Orange", "Teal", "Lime", "Violet & Lime", "Navy & Gold", "Crimson & Ice", "Orchid & Mint", "Teal & Coral", "Magenta & Spring", "Amber & Indigo", "Cyan & Sunset", "Rose & Jade", "Azure & Peach" };

    public string VersionCodename => CurrentVersion.StartsWith("0.5") ? "Blue Orchid" : string.Empty;
    public string VersionLabel => string.IsNullOrEmpty(VersionCodename) ? $"v{CurrentVersion}" : $"v{CurrentVersion} \u201c{VersionCodename}\u201d";
    public string OsLabel => OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsLinux() ? "Linux" : OperatingSystem.IsMacOS() ? "macOS" : "Unknown";

    public bool IsWindows => OperatingSystem.IsWindows();
    public bool CanInstallVlc => IsWindows && VlcStatus == "Not installed";

    public string[] TrackRowStyleOptions => new[] { "Comfortable", "Compact", "Cozy" };
    public string[] FontScaleOptions => new[] { "Small", "Medium", "Large" };
    public string[] SidebarWidthOptions => new[] { "Narrow", "Normal", "Wide" };
    public string[] AudioQualityOptions => new[] { "best", "320", "192", "128", "96" };
    public string[] AudioFormatOptions => new[] { "mp3", "flac", "ogg", "m4a", "wav" };
    public string[] AIModelOptions => AIModelCatalog.AllIds;
    public string[] AIModelDisplayOptions => AIModelCatalog.All.Select(m => m.OllamaId).ToArray();
    public string[] MoodRefreshOptions => new[] { "Never", "Every hour", "Every 3 hours", "Daily" };
    public string[] AIConfidenceOptions => new[] { "50%", "60%", "70%", "80%", "90%" };
    public int[] ConcurrentDownloadOptions => new[] { 1, 2, 3, 4, 5 };
    public int[] SkipWindowOptions => new[] { 5, 10, 15, 20, 30 };
    public int[] SkipPenaltyCapOptions => new[] { 2, 3, 5, 10 };

    public string CurrentVersion => _updater.CurrentVersion;
    public string FadeDurationDisplay => $"{FadeOnPauseDurationMs} ms";
    public string CrossfadeDurationDisplay => $"{CrossfadeDurationSeconds} s";
    public string ScrobbleThresholdDisplay => $"Scrobble after {ScrobbleThreshold:P0} of track duration";
    public bool AIFeaturesControlsEnabled => AIFeaturesEnabled;

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
    public event Action? LastFmConnectRequested;
    public event Action? LastFmConfirmAuthRequested;
    public event Action? LastFmDisconnectRequested;
    public event Action? ClearYtDlpCacheRequested;
    public event Action<bool>? SweepOrphanedFilesRequested;
    public event Action? VacuumDatabaseRequested;
    public event Action? VerifyLinksRequested;
    public event Action? ForceCleanTitlesRequested;
    public event Action? MergeSimilarArtistsRequested;
    public event Action<bool>? RemoveDuplicatesRequested;
    public event Action? GenerateTagMoodPlaylistRequested;

    public SettingsViewModel(KeyStoreService keyStore, SecureDeleteService secureDelete, PreferencesService prefsService, LocalAIService localAI, PluginManager plugins)
    {
        _keyStore = keyStore;
        _secureDelete = secureDelete;
        _prefsService = prefsService;
        _localAI = localAI;
        _plugins = plugins;
        _updater = new UpdateService();
        _deps = new DependencyUpdateService();

        _youtubeApiKey = _keyStore.GetKey("YouTube") ?? string.Empty;
        _spotifyClientId = _keyStore.GetKey("Spotify:ClientId") ?? string.Empty;
        _spotifyClientSecret = _keyStore.GetKey("Spotify:ClientSecret") ?? string.Empty;
        _soundCloudClientId = _keyStore.GetKey("SoundCloud") ?? string.Empty;
        _lastFmApiKey = _keyStore.GetKey("LastFm") ?? string.Empty;
        _lastFmApiSecret = _keyStore.GetKey("LastFm:Secret") ?? string.Empty;
        _openWeatherApiKey = _keyStore.GetKey("OpenWeather") ?? string.Empty;

        var existingUsername = _keyStore.GetKey("LastFm:Username");
        if (!string.IsNullOrEmpty(existingUsername) && !string.IsNullOrEmpty(_keyStore.GetKey("LastFm:SessionKey")))
        {
            _lastFmUsername = existingUsername;
            _lastFmState = LastFmConnectionState.Connected;
        }

        DetectHardware();
        _ = ProbeOllamaOnStartupAsync();
        StartAIHealthCheck();

        PluginRows = new ObservableCollection<PluginRowViewModel>();
        foreach (var plugin in _plugins.Plugins)
        {
            PluginRows.Add(new PluginRowViewModel(plugin, enabled =>
            {
                switch (plugin.Name)
                {
                    case "yt-dlp Downloader": _prefsService.Update(p => p.EnableYtDlp = enabled); break;
                    case "Last.fm": _prefsService.Update(p => p.EnableLastFm = enabled); break;
                    case "OpenWeather": _prefsService.Update(p => p.EnableOpenWeather = enabled); break;
                    case "Ollama Local AI": _prefsService.Update(p => p.EnableOllama = enabled); break;
                }
            }));
        }
    }

    [RelayCommand] private void RefreshWeather() => RefreshWeatherRequested?.Invoke();

    [RelayCommand]
    private void SaveKeys()
    {
        if (!string.IsNullOrWhiteSpace(YouTubeApiKey)) _keyStore.SaveKey("YouTube", YouTubeApiKey);
        if (!string.IsNullOrWhiteSpace(SpotifyClientId)) _keyStore.SaveKey("Spotify:ClientId", SpotifyClientId);
        if (!string.IsNullOrWhiteSpace(SpotifyClientSecret)) _keyStore.SaveKey("Spotify:ClientSecret", SpotifyClientSecret);
        if (!string.IsNullOrWhiteSpace(SoundCloudClientId)) _keyStore.SaveKey("SoundCloud", SoundCloudClientId);
        if (!string.IsNullOrWhiteSpace(LastFmApiKey)) _keyStore.SaveKey("LastFm", LastFmApiKey);
        if (!string.IsNullOrWhiteSpace(LastFmApiSecret)) _keyStore.SaveKey("LastFm:Secret", LastFmApiSecret);
        if (!string.IsNullOrWhiteSpace(OpenWeatherApiKey)) _keyStore.SaveKey("OpenWeather", OpenWeatherApiKey);
        ToastService.Instance.Show("API keys saved securely.", ToastType.Success, scope: "settings-save");
    }

    [RelayCommand] private void DeleteApiKeys()
    {
        _secureDelete.DeleteApiKeys();
        YouTubeApiKey = SpotifyClientId = SpotifyClientSecret = SoundCloudClientId = LastFmApiKey = LastFmApiSecret = OpenWeatherApiKey = string.Empty;
    }

    [RelayCommand] private void DeleteLogs() => _secureDelete.DeleteLogs();

    [RelayCommand] private void DeleteEverything()
    {
        _secureDelete.DeleteEverything();
        YouTubeApiKey = SpotifyClientId = SpotifyClientSecret = SoundCloudClientId = LastFmApiKey = LastFmApiSecret = OpenWeatherApiKey = string.Empty;
    }

    [RelayCommand]
    private async Task BrowseDownloadDirAsync()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop || desktop.MainWindow == null) return;
        var folders = await desktop.MainWindow.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Select Download Directory" });
        if (folders.Count > 0) DownloadDirectory = folders[0].Path.LocalPath;
    }

    [RelayCommand]
    private async Task CheckForUpdateAsync()
    {
        IsCheckingUpdate = true;
        UpdateStatus = "Checking...";
        try
        {
            var result = await _updater.CheckForUpdateAsync();
            UpdateAvailable = result.IsUpdateAvailable;
            LatestVersion = result.LatestVersion;
            ReleaseUrl = result.ReleaseUrl;
            UpdateStatus = result.IsUpdateAvailable ? $"Update available: v{result.LatestVersion}" : $"You are up to date (v{result.CurrentVersion})";
        }
        finally { IsCheckingUpdate = false; }
    }

    [RelayCommand]
    private async Task DownloadUpdateAsync()
    {
        IsStagingUpdate = true;
        try
        {
            var rid = RuntimeInformation.RuntimeIdentifier;
            UpdateStaged = await _updater.StageUpdateAsync(rid);
            ToastService.Instance.Show(
                UpdateStaged ? "Update downloaded \u2014 restart to install." : "No matching release asset found.",
                UpdateStaged ? ToastType.Success : ToastType.Warning, scope: "update");
        }
        catch (Exception ex)
        {
            ToastService.Instance.Show($"Update download failed: {ex.Message}", ToastType.Error, scope: "update");
        }
        finally { IsStagingUpdate = false; }
    }

    [RelayCommand] private void GenerateTagMoodPlaylist() => GenerateTagMoodPlaylistRequested?.Invoke();
    [RelayCommand] private void RestartToUpdate() => _updater.LaunchUpdaterAndExit();

    [RelayCommand]
    private async Task InstallVlcAsync()
    {
        VlcStatus = "Installing...";
        VlcStatus = await _deps.InstallVlcAsync();
    }

    [RelayCommand]
    private async Task CopyDebugInfoAsync()
    {
        var text = $"NullWave v{CurrentVersion} \u201c{VersionCodename}\u201d\n" +
                   $"OS: {OsLabel} ({RuntimeInformation.OSDescription})\n" +
                   $"RID: {RuntimeInformation.RuntimeIdentifier}\n" +
                   $".NET: {Environment.Version}";
        try
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(text);
                ToastService.Instance.Show("Debug info copied to clipboard.", ToastType.Success);
            }
            else
            {
                ToastService.Instance.Show("Could not access clipboard.", ToastType.Warning);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[Settings] Failed to copy debug info to clipboard");
            ToastService.Instance.Show("Could not copy to clipboard.", ToastType.Warning);
        }
    }

    private int _versionTapCount;
    [RelayCommand]
    private void TapVersion()
    {
        _versionTapCount++;
        if (_versionTapCount >= 7)
        {
            _versionTapCount = 0;
            ToastService.Instance.Show("🌸 Blue Orchid blooms for the curious.", ToastType.Info);
        }
    }

    [RelayCommand]
    private void OpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[Settings] Failed to open URL: {Url}", url);
        }
    }

    [RelayCommand]
    private async Task UpdateYtDlpAsync()
    {
        IsUpdatingYtDlp = true;
        YtDlpStatus = "Updating yt-dlp...";
        try
        {
            await _deps.UpdateYtDlpAsync();
            var info = await _deps.GetYtDlpInfoAsync();
            YtDlpStatus = info.IsInstalled ? $"yt-dlp {info.InstalledVersion} (up to date)" : "yt-dlp not found";
        }
        finally { IsUpdatingYtDlp = false; }
    }

    [RelayCommand]
    private async Task CheckDependenciesAsync()
    {
        YtDlpStatus = VlcStatus = FfmpegStatus = DotNetStatus = "Checking...";
        var ytDlp = await _deps.GetYtDlpInfoAsync();
        var vlc = await _deps.GetVlcInfoAsync();
        var ffmpeg = await _deps.GetFfmpegInfoAsync();
        var dotNet = await _deps.GetDotNetInfoAsync();
        YtDlpStatus = ytDlp.IsInstalled ? ytDlp.InstalledVersion : "Not installed";
        VlcStatus = vlc.IsInstalled ? vlc.InstalledVersion : "Not installed";
        FfmpegStatus = ffmpeg.IsInstalled ? ffmpeg.InstalledVersion : "Not installed";
        DotNetStatus = dotNet.IsInstalled ? dotNet.InstalledVersion : "Not found";
    }

    [RelayCommand] private void OpenDataFolder() => OpenFolder(NullWavePaths.DataDir);
    [RelayCommand] private void OpenLogsFolder() => OpenFolder(NullWavePaths.LogsDir);

    [RelayCommand]
    private void OpenReleasePage()
    {
        if (!string.IsNullOrEmpty(ReleaseUrl))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = ReleaseUrl, UseShellExecute = true });
    }

    [RelayCommand] private void ClearThumbnails() { ThumbnailStatus = "Clearing thumbnails..."; ClearThumbnailsRequested?.Invoke(); }
    [RelayCommand] private void ClearYtDlpCache() { ClearYtDlpCacheRequested?.Invoke(); }
    [RelayCommand] private void RepairPaths() { IsRepairing = true; RepairStatus = "Scanning file paths..."; RepairPathsRequested?.Invoke(); }
    [RelayCommand] private void ReimportAssets() { IsRepairing = true; RepairStatus = "Scanning download folder..."; ReimportAssetsRequested?.Invoke(); }
    [RelayCommand] private void ForceMetaResync() { IsRepairing = true; RepairStatus = "Clearing cached tags - re-sync starting..."; ForceMetaResyncRequested?.Invoke(); }
    [RelayCommand] private void PreviewOrphanedFiles() { IsRepairing = true; SweepStatus = "Scanning for orphaned files..."; SweepOrphanedFilesRequested?.Invoke(true); }
    [RelayCommand] private void SweepOrphanedFiles() { IsRepairing = true; SweepStatus = "Deleting orphaned files..."; SweepOrphanedFilesRequested?.Invoke(false); }
    [RelayCommand] private void VacuumDatabase() { IsRepairing = true; VacuumStatus = "Optimizing database..."; VacuumDatabaseRequested?.Invoke(); }
    [RelayCommand] private void VerifyLinks() { IsRepairing = true; VerifyLinksStatus = "Checking file links against embedded metadata..."; VerifyLinksRequested?.Invoke(); }
    [RelayCommand] private void ForceCleanTitles() { IsRepairing = true; ForceCleanStatus = "Re-parsing track titles for embedded artist names..."; ForceCleanTitlesRequested?.Invoke(); }
    [RelayCommand] private void MergeSimilarArtists() { MergeSimilarArtistsRequested?.Invoke(); }
    [RelayCommand] private void PreviewDuplicates() { IsRepairing = true; DedupeStatus = "Scanning for duplicate tracks..."; RemoveDuplicatesRequested?.Invoke(true); }
    [RelayCommand] private void RemoveDuplicates() { IsRepairing = true; DedupeStatus = "Removing duplicate tracks..."; RemoveDuplicatesRequested?.Invoke(false); }

    public void ReportDedupeComplete(int scanned, int groups, int removed, bool wasDryRun)
    {
        IsRepairing = false;
        DedupeStatus = wasDryRun
            ? $"Found {groups} duplicate group(s) out of {scanned} scanned."
            : $"Removed {removed} duplicate track(s) across {groups} group(s).";
    }

    public void ReportSweepComplete(int scanned, int orphaned, int deleted, int failed, bool wasDryRun)
    {
        IsRepairing = false;
        SweepStatus = wasDryRun ? $"Found {orphaned} orphaned file(s)." : $"Deleted {deleted}, {failed} failed.";
    }

    public void ReportVacuumComplete(long beforeKB, long afterKB)
    {
        IsRepairing = false;
        VacuumStatus = $"Optimized: {beforeKB}KB → {afterKB}KB";
    }

    public void ReportVerifyLinksComplete(int checkedCount, int mismatchCount)
    {
        IsRepairing = false;
        VerifyLinksStatus = $"Checked {checkedCount}, {mismatchCount} mismatch(es).";
    }

    public void ReportForceCleanComplete(int cleaned)
    {
        IsRepairing = false;
        ForceCleanStatus = cleaned == 0 ? "No titles needed cleaning." : $"Cleaned {cleaned} title(s).";
    }

    public void ReportArtistMergeComplete(int groupsFound, int tracksUpdated)
    {
        Log.Information("[Settings] Artist merge complete: {Groups} groups, {Tracks} tracks updated", groupsFound, tracksUpdated);
    }

    public void ReportRepairPathsComplete(int total, int missing, int cleared)
    {
        IsRepairing = false;
        RepairStatus = missing == 0 ? $"All {total} file paths are valid." : $"{missing} missing, {cleared} cleared.";
    }

    public void ReportReimportComplete(int relinked)
    {
        IsRepairing = false;
        RepairStatus = relinked == 0 ? "No new file matches found." : $"Re-linked {relinked} track(s).";
    }

    public void ReportMetaResyncComplete(int cleared)
    {
        IsRepairing = false;
        RepairStatus = $"Cleared tags for {cleared} track(s).";
    }

    public void ReportRepairFailed(string operation, string reason)
    {
        IsRepairing = false;
        RepairStatus = $"{operation} failed: {reason}";
    }

    public void ReportLastFmAwaitingAuth() { LastFmState = LastFmConnectionState.AwaitingAuth; LastFmStatusMessage = "Browser opened \u2014 approve access, then click \"I've Authorized It\"."; }

    public void ReportLastFmConnected(string username)
    {
        LastFmUsername = username;
        LastFmState = LastFmConnectionState.Connected;
        LastFmStatusMessage = string.Empty;
        ToastService.Instance.Show($"Connected to Last.fm as {username}", ToastType.Success);
    }

    public void ReportLastFmDisconnected()
    {
        LastFmUsername = string.Empty;
        LastFmState = LastFmConnectionState.Disconnected;
        LastFmStatusMessage = string.Empty;
        ToastService.Instance.Show("Disconnected from Last.fm", ToastType.Info);
    }

    public void ReportLastFmAuthFailed(string reason)
    {
        LastFmState = LastFmConnectionState.Error;
        LastFmStatusMessage = reason;
        ToastService.Instance.Show($"Last.fm: {reason}", ToastType.Error);
    }

    public async Task ReportExportReadyAsync(IEnumerable<Track> tracks, Avalonia.Controls.Window parentWindow)
    {
        var trackList = tracks.ToList();
        if (trackList.Count == 0) { ExternalAIStatus = "No untagged tracks found."; return; }
        var format = ExportFormat ?? "txt";
        var timestamp = DateTime.Now.ToString("ddMMyyyy_HHmm");
        var baseFileName = $"nullwave_ai_prompt_{timestamp}.{format}";
        var chunks = _externalAI.GenerateChunked(trackList, format, baseFileName);
        var sp = new FilePickerSaveOptions
        {
            Title = chunks.Count > 1 ? $"Save AI Prompt - Part 1 of {chunks.Count}" : "Save AI Tagging Prompt",
            SuggestedFileName = chunks[0].FileName,
            FileTypeChoices = new[] { new FilePickerFileType("Text") { Patterns = new[] { "*.txt" } }, new FilePickerFileType("Markdown") { Patterns = new[] { "*.md" } }, new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } } }
        };
        int savedCount = 0;
        foreach (var (content, fileName) in chunks)
        {
            sp.SuggestedFileName = fileName;
            if (savedCount > 0) sp.Title = $"Save AI Prompt - Part {savedCount + 1} of {chunks.Count}";
            var file = await parentWindow.StorageProvider.SaveFilePickerAsync(sp);
            if (file == null) { ExternalAIStatus = savedCount == 0 ? "Export cancelled." : $"Partial export - saved {savedCount} of {chunks.Count} files."; return; }
            try { await using var stream = await file.OpenWriteAsync(); await using var writer = new System.IO.StreamWriter(stream); await writer.WriteAsync(content); savedCount++; }
            catch (Exception ex) { ExternalAIStatus = $"Export failed on part {savedCount + 1}: {ex.Message}"; return; }
        }
        ExternalAIStatus = chunks.Count > 1 ? $"Exported {trackList.Count} tracks in {chunks.Count} files" : $"Exported {trackList.Count} tracks \u2192 {chunks[0].FileName}";
        ToastService.Instance.Show(ExternalAIStatus, ToastType.Success);
    }

    public void ReportImportComplete(int applied, int total)
    {
        if (applied == 0 && total == 0) { ExternalAIStatus = "Import cancelled or file was empty."; return; }
        ExternalAIStatus = total == 0 ? "No matching tracks found in import." : $"Import complete - tagged {applied} of {total} tracks.";
        ToastService.Instance.Show(ExternalAIStatus, applied > 0 ? ToastType.Success : ToastType.Warning);
    }

    public void ReportImportFailed(string reason)
    {
        ExternalAIStatus = $"Import failed: {reason}";
        ToastService.Instance.Show($"Import failed: {reason}", ToastType.Error);
    }

    [RelayCommand]
    private void DetectHardware()
    {
        IsDetectingHardware = true;
        try
        {
            var detector = new HardwareDetector();
            var info = detector.Detect();
            HardwareInfo = $"CPU: {info.CpuCores} cores | RAM: {info.RamGB}GB\nGPU: {info.GpuType} ({info.GpuVramGB}GB VRAM)\nRecommended: {info.RecommendedModel}\n{info.RecommendationReason}";
            var currentPrefs = _prefsService.Current;
            if (string.IsNullOrEmpty(currentPrefs.SelectedAIModel)) SelectedModel = info.RecommendedModel;
            else OnPropertyChanged(nameof(SelectedModel));

            var suggestedBattery = AIModelCatalog.SuggestBatteryModel(info.RamGB);
            var suggestedPerf = AIModelCatalog.SuggestPerformanceModel(info.RamGB, info.GpuVramGB, info.HasNvidia || info.HasAmd);
            if (string.IsNullOrEmpty(currentPrefs.BatteryModel)) BatteryModel = suggestedBattery;
            else OnPropertyChanged(nameof(BatteryModel));

            if (string.IsNullOrEmpty(currentPrefs.PerformanceModel)) PerformanceModel = suggestedPerf;
            else OnPropertyChanged(nameof(PerformanceModel));

            UpdatePowerState();
        }
        catch (Exception ex) { HardwareInfo = $"Detection failed: {ex.Message}"; }
        finally { IsDetectingHardware = false; }
    }

    private void UpdatePowerState()
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                bool onBattery = true;
                const string sysfsPath = "/sys/class/power_supply";
                if (System.IO.Directory.Exists(sysfsPath))
                {
                    foreach (var dir in System.IO.Directory.GetDirectories(sysfsPath))
                    {
                        if (dir.Contains("AC") || dir.Contains("ADP") || dir.Contains("ACAD"))
                        {
                            var onlineFile = System.IO.Path.Combine(dir, "online");
                            if (System.IO.File.Exists(onlineFile) && System.IO.File.ReadAllText(onlineFile).Trim() == "1")
                            {
                                onBattery = false;
                                break;
                            }
                        }
                    }
                }
                PowerStateLabel = onBattery ? "Battery (Power Saver)" : "AC Power (Performance Mode)";
            }
            else if (OperatingSystem.IsWindows())
            {
                PowerStateLabel = "AC Power Connected";
            }
            else
            {
                PowerStateLabel = "AC Power Source";
            }
        }
        catch
        {
            PowerStateLabel = "Unknown Power State";
        }
    }

    [RelayCommand]
    private async Task DownloadModelAsync()
    {
        if (IsDownloadingModel) return;
        IsDownloadingModel = true;
        ModelDownloadProgress = 0;
        ModelDownloadStatus = $"Downloading {SelectedModel}...";
        try
        {
            var progress = new Progress<double>(pct => { ModelDownloadProgress = pct * 100; ModelDownloadStatus = $"Downloading {SelectedModel}... {pct:P0}"; });
            await _localAI.DownloadModelAsync(SelectedModel, progress);
            ModelDownloadStatus = $"\u2713 {SelectedModel} downloaded successfully";
            await ToggleAIServiceAsync();
        }
        catch (Exception ex) { ModelDownloadStatus = $" Download failed: {ex.Message}"; }
        finally { IsDownloadingModel = false; }
    }

    [RelayCommand] private void GenerateMoodPlaylist() { MoodPlaylistStatus = "Generating mood playlist..."; GenerateMoodPlaylistRequested?.Invoke(); }

    [RelayCommand]
    private async Task ToggleAIServiceAsync()
    {
        if (!AIFeaturesEnabled) { AiServiceState = AIServiceState.Stopped; return; }
        if (AiServiceState == AIServiceState.Running) { AiServiceState = AIServiceState.Stopped; return; }
        AiServiceState = AIServiceState.Starting;
        try
        {
            _localAI.CurrentModel = SelectedModel;
            bool ok = await _localAI.PingAsync();
            AiServiceState = ok ? AIServiceState.Running : AIServiceState.Error;
        }
        catch { AiServiceState = AIServiceState.Error; }
    }

    [RelayCommand] private async Task ExportUntaggedTracksAsync() { ExternalAIStatus = "Preparing export..."; ExportUntaggedTracksRequested?.Invoke(); await Task.CompletedTask; }
    [RelayCommand] private async Task ImportAiTagsAsync() { var task = ImportAiTagsRequested?.Invoke(); if (task != null) await task; }
    [RelayCommand] private void NavigateSettings(string page) { if (!string.IsNullOrWhiteSpace(page)) CurrentSettingsPage = page; }
    [RelayCommand] private void LastFmConnect() { LastFmState = LastFmConnectionState.AwaitingAuth; LastFmStatusMessage = "Opening browser for Last.fm authorization..."; LastFmConnectRequested?.Invoke(); }
    [RelayCommand] private void LastFmConfirmAuth() => LastFmConfirmAuthRequested?.Invoke();
    [RelayCommand] private void LastFmDisconnect() => LastFmDisconnectRequested?.Invoke();

    private async Task ProbeOllamaOnStartupAsync()
    {
        try
        {
            if (!AIFeaturesEnabled) { AiServiceState = AIServiceState.Stopped; return; }
            bool running = await _localAI.PingAsync();
            if (AiServiceState == AIServiceState.Stopped) AiServiceState = running ? AIServiceState.Running : AIServiceState.Stopped;
            if (_plugins.Get<OllamaAIProvider>() is { } ollama) ollama.State = running ? PluginState.Available : PluginState.Unavailable;
        }
        catch { }
    }

    public void StartAIHealthCheck() { _aiHealthTimer = new System.Threading.Timer(async _ => await HealthCheckTickAsync(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30)); }

    private async Task HealthCheckTickAsync()
    {
        try
        {
            if (!AIFeaturesEnabled || AiServiceState == AIServiceState.Stopped) return;
            bool reachable = await _localAI.PingAsync();
            var newState = reachable ? AIServiceState.Running : AIServiceState.Error;
            if (AiServiceState != newState) AiServiceState = newState;
            if (_plugins.Get<OllamaAIProvider>() is { } ollama) ollama.State = reachable ? PluginState.Available : PluginState.Error;
        }
        catch { }
    }

    public void StopHealthCheck() { _aiHealthTimer?.Dispose(); _aiHealthTimer = null; }
    public void SetAIServiceState(AIServiceState state) => AiServiceState = state;
    public void ReportThumbnailsCleared(int count) { ThumbnailStatus = $"Cleared {count} thumbnails - re-fetching in background..."; }
    public void ReportMoodPlaylistGenerated(int trackCount, string mood) { MoodPlaylistStatus = $"Generated {trackCount} tracks for mood: {mood}"; }
    public void ReportMoodPlaylistFailed(string reason) { MoodPlaylistStatus = $"Failed: {reason}"; }

    private static void OpenFolder(string path)
    {
        System.IO.Directory.CreateDirectory(path);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = path, UseShellExecute = true });
    }

    public void Dispose()
    {
        StopHealthCheck();
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        GC.SuppressFinalize(this);
    }
}