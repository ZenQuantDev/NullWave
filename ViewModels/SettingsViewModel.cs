using System;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using NullWave.Helpers;
using NullWave.Services;
using NullWave.ViewModels.Base;
using Serilog;

namespace NullWave.ViewModels;

public class SettingsViewModel : ViewModelBase
{
    private readonly KeyStoreService _keyStore;
    private readonly SecureDeleteService _secureDelete;
    private readonly PreferencesService _prefsService;
    private readonly UpdateService _updater;
    private readonly DependencyUpdateService _deps;

    // API Keys
    private string _youtubeApiKey = string.Empty;
    private string _spotifyClientId = string.Empty;
    private string _spotifyClientSecret = string.Empty;
    private string _soundCloudClientId = string.Empty;
    private string _lastFmApiKey = string.Empty;

    // Update status
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

    public string YouTubeApiKey { get => _youtubeApiKey; set { _youtubeApiKey = value; OnPropertyChanged(); } }
    public string SpotifyClientId { get => _spotifyClientId; set { _spotifyClientId = value; OnPropertyChanged(); } }
    public string SpotifyClientSecret { get => _spotifyClientSecret; set { _spotifyClientSecret = value; OnPropertyChanged(); } }
    public string SoundCloudClientId { get => _soundCloudClientId; set { _soundCloudClientId = value; OnPropertyChanged(); } }
    public string LastFmApiKey { get => _lastFmApiKey; set { _lastFmApiKey = value; OnPropertyChanged(); } }

    public string AudioQuality
    {
        get => _prefsService.Current.AudioQuality;
        set { _prefsService.Update(p => p.AudioQuality = value); OnPropertyChanged(); }
    }

    public string AudioFormat
    {
        get => _prefsService.Current.AudioFormat;
        set { _prefsService.Update(p => p.AudioFormat = value); OnPropertyChanged(); }
    }

    public string DownloadDirectory
    {
        get => _prefsService.Current.DownloadDirectory;
        set { _prefsService.Update(p => p.DownloadDirectory = value); OnPropertyChanged(); }
    }

    public bool AutoFetchMetadata
    {
        get => _prefsService.Current.AutoFetchMetadata;
        set { _prefsService.Update(p => p.AutoFetchMetadata = value); OnPropertyChanged(); }
    }

    public bool AutoPlayNext
    {
        get => _prefsService.Current.AutoPlayNext;
        set { _prefsService.Update(p => p.AutoPlayNext = value); OnPropertyChanged(); }
    }

    public bool DownloadOnAdd
    {
        get => _prefsService.Current.DownloadOnAdd;
        set { _prefsService.Update(p => p.DownloadOnAdd = value); OnPropertyChanged(); }
    }

    public bool ScrobbleToLastFm
    {
        get => _prefsService.Current.ScrobbleToLastFm;
        set { _prefsService.Update(p => p.ScrobbleToLastFm = value); OnPropertyChanged(); }
    }
    public string AccentColor
    {
        get => _prefsService.Current.AccentColor;
        set { _prefsService.Update(p => p.AccentColor = value); OnPropertyChanged(); }
    }

    public string TrackRowStyle
    {
        get => _prefsService.Current.TrackRowStyle;
        set { _prefsService.Update(p => p.TrackRowStyle = value); OnPropertyChanged(); }
    }

    public string FontScale
    {
        get => _prefsService.Current.FontScale;
        set { _prefsService.Update(p => p.FontScale = value); OnPropertyChanged(); }
    }

    public bool CompactMode
    {
        get => _prefsService.Current.CompactMode;
        set { _prefsService.Update(p => p.CompactMode = value); OnPropertyChanged(); }
    }

    public string SidebarWidth
    {
        get => _prefsService.Current.SidebarWidth;
        set { _prefsService.Update(p => p.SidebarWidth = value); OnPropertyChanged(); }
    }

    public string[] AccentColorOptions  => new[] { "Purple", "Blue", "Amber", "Green", "Red" };
    public string[] TrackRowStyleOptions => new[] { "Comfortable", "Compact", "Cozy" };
    public string[] FontScaleOptions     => new[] { "Small", "Medium", "Large" };
    public string[] SidebarWidthOptions  => new[] { "Narrow", "Normal", "Wide" };

    public string[] AudioQualityOptions => new[] { "best", "320", "192", "128", "96" };
    public string[] AudioFormatOptions => new[] { "mp3", "flac", "ogg", "m4a", "wav" };

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

        _youtubeApiKey = _keyStore.GetKey("YouTube") ?? string.Empty;
        _spotifyClientId = _keyStore.GetKey("Spotify:ClientId") ?? string.Empty;
        _spotifyClientSecret = _keyStore.GetKey("Spotify:ClientSecret") ?? string.Empty;
        _soundCloudClientId = _keyStore.GetKey("SoundCloud") ?? string.Empty;
        _lastFmApiKey = _keyStore.GetKey("LastFm") ?? string.Empty;

        SaveKeysCommand = new RelayCommand(SaveKeys);
        DeleteApiKeysCommand = new RelayCommand(DeleteApiKeys);
        DeleteLogsCommand = new RelayCommand(DeleteLogs);
        DeleteEverythingCommand = new RelayCommand(DeleteEverything);
        BrowseDownloadDirCommand = new RelayCommand(async () => await BrowseDownloadDirAsync());
        CheckForUpdateCommand = new RelayCommand(async () => await CheckForUpdateAsync());
        OpenReleasePageCommand = new RelayCommand(() =>
        {
            if (!string.IsNullOrEmpty(_releaseUrl))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = _releaseUrl,
                    UseShellExecute = true
                });
        });
        UpdateYtDlpCommand = new RelayCommand(async () => await UpdateYtDlpAsync());
        CheckDependenciesCommand = new RelayCommand(async () => await CheckDependenciesAsync());
        OpenDataFolderCommand = new RelayCommand(() =>
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = NullWavePaths.DataDir,
                UseShellExecute = true
            }));
        OpenLogsFolderCommand = new RelayCommand(() =>
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = NullWavePaths.LogsDir,
                UseShellExecute = true
            }));
    }

    private void SaveKeys()
    {
        if (!string.IsNullOrWhiteSpace(YouTubeApiKey)) _keyStore.SaveKey("YouTube", YouTubeApiKey);
        if (!string.IsNullOrWhiteSpace(SpotifyClientId)) _keyStore.SaveKey("Spotify:ClientId", SpotifyClientId);
        if (!string.IsNullOrWhiteSpace(SpotifyClientSecret)) _keyStore.SaveKey("Spotify:ClientSecret", SpotifyClientSecret);
        if (!string.IsNullOrWhiteSpace(SoundCloudClientId)) _keyStore.SaveKey("SoundCloud", SoundCloudClientId);
        if (!string.IsNullOrWhiteSpace(LastFmApiKey)) _keyStore.SaveKey("LastFm", LastFmApiKey);
        Log.Information("[Settings] API keys saved");
    }

    private void DeleteApiKeys()
    {
        _secureDelete.DeleteApiKeys();
        YouTubeApiKey = SpotifyClientId = SpotifyClientSecret = SoundCloudClientId = LastFmApiKey = string.Empty;
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
        YouTubeApiKey = SpotifyClientId = SpotifyClientSecret = SoundCloudClientId = LastFmApiKey = string.Empty;
        Log.Warning("[Settings] Full data wipe performed");
    }

    private async Task BrowseDownloadDirAsync()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
            desktop.MainWindow == null) return;

        var folders = await desktop.MainWindow.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Download Directory"
        });

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
            LatestVersion = result.LatestVersion;
            ReleaseUrl = result.ReleaseUrl;

            if (result.IsUpdateAvailable)
                UpdateStatus = $"Update available: v{result.LatestVersion} (published {result.PublishedAt:dd-MMM-yyyy})";
            else if (result.LatestVersion == "unknown")
                UpdateStatus = "Could not reach GitHub — check your connection";
            else
                UpdateStatus = $"You are up to date (v{result.CurrentVersion})";
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
            YtDlpStatus = info.IsInstalled ? $"yt-dlp {info.InstalledVersion} (up to date)" : "yt-dlp not found";
        }
        finally
        {
            IsUpdatingYtDlp = false;
        }
    }

    private async Task CheckDependenciesAsync()
    {
        YtDlpStatus = "Checking...";
        VlcStatus = "Checking...";
        FfmpegStatus = "Checking...";
        DotNetStatus = "Checking...";

        var ytDlp = await _deps.GetYtDlpInfoAsync();
        var vlc = await _deps.GetVlcInfoAsync();
        var ffmpeg = await _deps.GetFfmpegInfoAsync();
        var dotNet = await _deps.GetDotNetInfoAsync();

        YtDlpStatus = ytDlp.IsInstalled ? $"{ytDlp.InstalledVersion}" : "Not installed";
        VlcStatus = vlc.IsInstalled ? $"{vlc.InstalledVersion}" : "Not installed";
        FfmpegStatus = ffmpeg.IsInstalled ? $"{ffmpeg.InstalledVersion}" : "Not installed";
        DotNetStatus = dotNet.IsInstalled ? $"{dotNet.InstalledVersion}" : "Not found";
    }
}