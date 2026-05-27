using System.Windows.Input;
using NullWave.Helpers;
using NullWave.Services;
using NullWave.ViewModels.Base;
using Serilog;

namespace NullWave.ViewModels;

public class SettingsViewModel : ViewModelBase
{
    private readonly KeyStoreService _keyStore;
    private readonly SecureDeleteService _secureDelete;

    private string _youtubeApiKey = string.Empty;
    private string _spotifyClientId = string.Empty;
    private string _spotifyClientSecret = string.Empty;
    private string _soundCloudClientId = string.Empty;
    private string _defaultExportPath = string.Empty;
    private bool _autoFetchMetadata = true;

    public ICommand SaveKeysCommand { get; }
    public ICommand DeleteApiKeysCommand { get; }
    public ICommand DeleteLogsCommand { get; }
    public ICommand DeleteEverythingCommand { get; }

    public string YouTubeApiKey
    {
        get => _youtubeApiKey;
        set { _youtubeApiKey = value; OnPropertyChanged(); }
    }

    public string SpotifyClientId
    {
        get => _spotifyClientId;
        set { _spotifyClientId = value; OnPropertyChanged(); }
    }

    public string SpotifyClientSecret
    {
        get => _spotifyClientSecret;
        set { _spotifyClientSecret = value; OnPropertyChanged(); }
    }

    public string SoundCloudClientId
    {
        get => _soundCloudClientId;
        set { _soundCloudClientId = value; OnPropertyChanged(); }
    }

    public string DefaultExportPath
    {
        get => _defaultExportPath;
        set { _defaultExportPath = value; OnPropertyChanged(); }
    }

    public bool AutoFetchMetadata
    {
        get => _autoFetchMetadata;
        set { _autoFetchMetadata = value; OnPropertyChanged(); }
    }

    public SettingsViewModel(KeyStoreService keyStore, SecureDeleteService secureDelete)
    {
        _keyStore = keyStore;
        _secureDelete = secureDelete;

        // Load existing keys into fields (masked display handled by UI)
        _youtubeApiKey = _keyStore.GetKey("YouTube") ?? string.Empty;
        _spotifyClientId = _keyStore.GetKey("Spotify:ClientId") ?? string.Empty;
        _spotifyClientSecret = _keyStore.GetKey("Spotify:ClientSecret") ?? string.Empty;
        _soundCloudClientId = _keyStore.GetKey("SoundCloud") ?? string.Empty;

        SaveKeysCommand = new RelayCommand(SaveKeys);
        DeleteApiKeysCommand = new RelayCommand(DeleteApiKeys);
        DeleteLogsCommand = new RelayCommand(DeleteLogs);
        DeleteEverythingCommand = new RelayCommand(DeleteEverything);
    }

    private void SaveKeys()
    {
        if (!string.IsNullOrWhiteSpace(YouTubeApiKey))
            _keyStore.SaveKey("YouTube", YouTubeApiKey);
        if (!string.IsNullOrWhiteSpace(SpotifyClientId))
            _keyStore.SaveKey("Spotify:ClientId", SpotifyClientId);
        if (!string.IsNullOrWhiteSpace(SpotifyClientSecret))
            _keyStore.SaveKey("Spotify:ClientSecret", SpotifyClientSecret);
        if (!string.IsNullOrWhiteSpace(SoundCloudClientId))
            _keyStore.SaveKey("SoundCloud", SoundCloudClientId);

        Log.Information("API keys saved from Settings");
    }

    private void DeleteApiKeys()
    {
        _secureDelete.DeleteApiKeys();
        YouTubeApiKey = string.Empty;
        SpotifyClientId = string.Empty;
        SpotifyClientSecret = string.Empty;
        SoundCloudClientId = string.Empty;
        Log.Warning("All API keys deleted by user");
    }

    private void DeleteLogs()
    {
        _secureDelete.DeleteLogs();
        Log.Warning("Logs deleted by user");
    }

    private void DeleteEverything()
    {
        _secureDelete.DeleteEverything();
        YouTubeApiKey = string.Empty;
        SpotifyClientId = string.Empty;
        SpotifyClientSecret = string.Empty;
        SoundCloudClientId = string.Empty;
        Log.Warning("Full data wipe performed by user");
    }
}