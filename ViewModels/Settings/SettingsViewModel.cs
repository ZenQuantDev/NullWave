using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NullWave.Helpers;
using NullWave.Helpers.Logging;
using NullWave.Models;
using NullWave.Services;
using NullWave.Services.Plugins;
using NullWave.Services.Security;
using NullWave.Services.SmartSorting;
using NullWave.ViewModels.Settings;
using Serilog;
using Serilog.Events;

namespace NullWave.ViewModels;

public enum AIServiceState { Stopped, Starting, Running, Error }
public enum LastFmConnectionState { Disconnected, AwaitingAuth, Connected, Error }

public partial class SettingsViewModel : ObservableObject, IDisposable
{
    #region Fields & Services
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
    #endregion

    #region Constructor
    public SettingsViewModel(KeyStoreService keyStore, SecureDeleteService secureDelete, PreferencesService prefsService, LocalAIService localAI, PluginManager plugins)
    {
        IsDevMode = CheckDevAccess();
        _keyStore = keyStore;
        _secureDelete = secureDelete;
        _prefsService = prefsService;
        _localAI = localAI;
        _plugins = plugins;
        _updater = new UpdateService();
        _deps = new DependencyUpdateService();
        
        _isSettingsSidebarCollapsed = prefsService.Current.SettingsSidebarCollapsed;
        _selectedLanguage = prefsService.Current.Language;
        
        // API Keys Init
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
        LogViewer = new LogViewerViewModel(_prefsService.Current.VerboseLogging, BuildSettingsSummary);
    }
    #endregion

    #region Helpers
    public string BuildDiagnosticsText()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"App Version: {VersionLabel}");
        sb.AppendLine($"OS: {OsLabel} ({RuntimeInformation.OSDescription})");
        sb.AppendLine($"Architecture: {RuntimeInformation.OSArchitecture} (Process: {RuntimeInformation.ProcessArchitecture})");
        sb.AppendLine($"Runtime: {RuntimeInformation.FrameworkDescription}");
        sb.AppendLine($"Language: {SelectedLanguage}");
        sb.AppendLine($"Data Directory: {NullWavePaths.DataDir}");
        sb.AppendLine($"Logs Directory: {NullWavePaths.LogsDir}");
        sb.AppendLine($"Timestamp: {DateTime.UtcNow:dd-MM-yyyy HH:mm:ss} UTC");
        return sb.ToString();
    }

    private static string L(string key) => LocalizationService.Instance[key];
    
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
            catch (OperationCanceledException) { }
            catch (Exception ex) { Log.Error(ex, "[Settings] Critical error trying to save."); }
        }, token);
    }

    private static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = path, UseShellExecute = true });
    }
    #endregion

    #region UI State & Navigation
    public ObservableCollection<LiveNotification> ActiveToasts => ToastService.Instance.ActiveToasts;
    public ObservableCollection<PluginRowViewModel> PluginRows { get; private set; } = new();

    private bool _isSettingsSidebarCollapsed;
    public bool IsSettingsSidebarCollapsed
    {
        get => _isSettingsSidebarCollapsed;
        set
        {
            if (_isSettingsSidebarCollapsed == value) return;
            _isSettingsSidebarCollapsed = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSettingsSidebarExpanded));
            OnPropertyChanged(nameof(SettingsSidebarWidth));
            OnPropertyChanged(nameof(SettingsSidebarCollapseIconKind));
            _prefsService.Update(p => p.SettingsSidebarCollapsed = value);
            ScheduleSave();
        }
    }
    public bool IsSettingsSidebarExpanded => !IsSettingsSidebarCollapsed;
    public double SettingsSidebarWidth => IsSettingsSidebarCollapsed ? 69 : 220;
    public Material.Icons.MaterialIconKind SettingsSidebarCollapseIconKind =>
        IsSettingsSidebarCollapsed ? Material.Icons.MaterialIconKind.ChevronRight : Material.Icons.MaterialIconKind.ChevronLeft;

    [ObservableProperty] private int _currentSectionIndex = 0;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentSettingsPageTitle))]
    [NotifyPropertyChangedFor(nameof(CurrentSettingsPageDescription))]
    private string _currentSettingsPage = "General";

    public string CurrentSettingsPageTitle => LocalizationService.Instance[$"Settings_PageTitle_{CurrentSettingsPage}"];
    public string CurrentSettingsPageDescription => LocalizationService.Instance[$"Settings_PageDesc_{CurrentSettingsPage}"];

    [ObservableProperty] private bool _isKeyRevealed;
    [ObservableProperty] private bool _isRepairing = false;

    [ObservableProperty] private string _selectedLanguage = "en-US";
    public List<KeyValuePair<string, string>> SupportedLanguages => LocalizationService.SupportedLanguages;
    public bool IsLanguageApplyVisible => SelectedLanguage != LocalizationService.Instance.CurrentLanguage;
    
    partial void OnSelectedLanguageChanged(string value)
    {
        LocalizationService.Instance.SetLanguage(value);
        OnPropertyChanged(nameof(IsLanguageApplyVisible));
    }

    public bool HasCompletedOnboarding => _prefsService.Current.HasCompletedOnboarding;
    public string CurrentVersion => _updater.CurrentVersion;
    public string VersionCodename => CurrentVersion.StartsWith("0.6") ? "Oxeye Daisy" : string.Empty;
    public string VersionLabel => string.IsNullOrEmpty(VersionCodename) ? $"v{CurrentVersion}" : $"v{CurrentVersion} “{VersionCodename}”";
    public string OsLabel => OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsLinux() ? "Linux" : OperatingSystem.IsMacOS() ? "macOS" : "Unknown";
    public bool IsWindows => OperatingSystem.IsWindows();
    #endregion

    #region Dropdown Options
    public string[] BrowserCookieOptions => new[] { "", "firefox", "chrome", "chromium", "brave", "vivaldi", "edge" };
    public string[] ExportFormatOptions => new[] { "txt", "md", "json" };
    public string[] AccentColorOptions => new[] { "Oxeye Daisy", "Purple", "Sky", "Green", "Amber", "Red", "Pink", "Orange", "Teal", "Lime", "Violet & Lime", "Navy & Gold", "Crimson & Ice", "Orchid & Mint", "Teal & Coral", "Magenta & Spring", "Amber & Indigo", "Cyan & Sunset", "Rose & Jade", "Azure & Peach" };
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
    public int[] BackupRetentionOptions => new[] { 1, 3, 5, 7, 10 };
    #endregion

    #region Events
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
    public event Action<bool>? SyncFilesRequested;
    public event Action? BackfillDurationsRequested;
    public event Action? ImportExistingLibraryRequested;
    public event Action? RestoreDatabaseRequested;
    #endregion

    #region Commands - General & Appearance
    [RelayCommand] private async Task BrowseDownloadDirAsync()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop || desktop.MainWindow == null) return;
        var folders = await desktop.MainWindow.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Select Download Directory" });
        if (folders.Count > 0) DownloadDirectory = folders[0].Path.LocalPath;
    }
    
    [RelayCommand] private void OpenDataFolder() => OpenFolder(NullWavePaths.DataDir);
    [RelayCommand] private void OpenLogsFolder() => OpenFolder(NullWavePaths.LogsDir);
    
    [RelayCommand] private void ApplyLanguage()
    {
        LocalizationService.Instance.SetLanguage(SelectedLanguage);
        _prefsService.Update(p => p.Language = SelectedLanguage);
        OnPropertyChanged(nameof(IsLanguageApplyVisible));
        ScheduleSave();
        ToastService.Instance.Show(LocalizationService.Instance["Settings_General_Language_Updated"], ToastType.Success);
    }

    [RelayCommand] private void CompleteOnboarding()
    {
        _prefsService.Update(p => p.HasCompletedOnboarding = true);
        OnPropertyChanged(nameof(HasCompletedOnboarding));
        ScheduleSave();
    }

    [RelayCommand] private void ImportExistingLibrary() => ImportExistingLibraryRequested?.Invoke();
    [RelayCommand] private void ToggleKeyRevealed() => IsKeyRevealed = !IsKeyRevealed;
    [RelayCommand] private void ToggleSettingsSidebar() => IsSettingsSidebarCollapsed = !IsSettingsSidebarCollapsed;
    [RelayCommand] private void NavigateSettings(string page) { if (!string.IsNullOrWhiteSpace(page)) CurrentSettingsPage = page; }
    
    [RelayCommand] private async Task BackupDatabaseAsync()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop || desktop.MainWindow == null) return;
        var dbPath = NullWavePaths.DatabasePath;
        var file = await desktop.MainWindow.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Backup Library Database",
            SuggestedFileName = $"nullwave_backup_{DateTime.Now:yyyyMMdd_HHmm}.db",
            FileTypeChoices = new[] { new FilePickerFileType("SQLite Database") { Patterns = new[] { "*.db" } } }
        });
        if (file != null)
        {
            try
            {
                File.Copy(dbPath, file.Path.LocalPath, overwrite: true);
                ToastService.Instance.Show($"Database backed up successfully to {file.Name}.", ToastType.Success);
            }
            catch (Exception ex) { ToastService.Instance.Show($"Backup failed: {ex.Message}", ToastType.Error); }
        }
    }

    [RelayCommand] private void SetThemeMode(string mode) => ThemeMode = mode;
    [RelayCommand] private void SetAccent(string name) => AccentColor = name;
    [RelayCommand] private void SetRowStyle(string style) => TrackRowStyle = style;
    [RelayCommand] private void SetFontScale(string v) => FontScale = v;
    [RelayCommand] private void SetSidebarWidth(string v) => SidebarWidth = v;
    [RelayCommand] private void SetProfileFrame(string style) => ProfileFrameStyle = style;
    #endregion

    #region IDisposable
    public void Dispose()
    {
        StopHealthCheck();
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        LogViewer?.Dispose();
        GC.SuppressFinalize(this);
    }
    #endregion
}