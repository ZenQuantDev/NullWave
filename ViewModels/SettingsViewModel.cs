using NullWave.ViewModels.Base;
namespace NullWave.ViewModels;

public class SettingsViewModel : ViewModelBase
{
    private string _youtubeApiKey = string.Empty;
    private string _lastFmApiKey = string.Empty;
    private string _spotifyClientId = string.Empty;
    private string _defaultExportPath = string.Empty;
    private bool _autoFetchMetadata = true;

    public string YouTubeApiKey
    {
        get => _youtubeApiKey;
        set { _youtubeApiKey = value; OnPropertyChanged(); }
    }

    public string LastFmApiKey
    {
        get => _lastFmApiKey;
        set { _lastFmApiKey = value; OnPropertyChanged(); }
    }

    public string SpotifyClientId
    {
        get => _spotifyClientId;
        set { _spotifyClientId = value; OnPropertyChanged(); }
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
}