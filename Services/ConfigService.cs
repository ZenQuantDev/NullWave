using System;

namespace NullWave.Services;

public class ConfigService
{
    private readonly KeyStoreService _keyStore;

    public ConfigService(KeyStoreService keyStore)
    {
        _keyStore = keyStore;
    }

    public string GetYouTubeApiKey()
        => _keyStore.GetKey("YouTube")
           ?? Environment.GetEnvironmentVariable("NULLWAVE_YOUTUBE_KEY")
           ?? string.Empty;

    public string GetSpotifyClientId()
        => _keyStore.GetKey("Spotify:ClientId")
           ?? Environment.GetEnvironmentVariable("NULLWAVE_SPOTIFY_CLIENT_ID")
           ?? string.Empty;

    public string GetSpotifyClientSecret()
        => _keyStore.GetKey("Spotify:ClientSecret")
           ?? Environment.GetEnvironmentVariable("NULLWAVE_SPOTIFY_CLIENT_SECRET")
           ?? string.Empty;

    public string GetSoundCloudClientId()
        => _keyStore.GetKey("SoundCloud")
           ?? Environment.GetEnvironmentVariable("NULLWAVE_SOUNDCLOUD_CLIENT_ID")
           ?? string.Empty;
}