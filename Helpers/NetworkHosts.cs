using System.Collections.Generic;

namespace NullWave.Helpers;

public record NetworkHost(string Host, string Purpose, string? PluginId = null, bool RequiresKey = false);

public static class NetworkHosts
{
    public static readonly IReadOnlyList<NetworkHost> All = new NetworkHost[]
    {
        new("api.openweathermap.org", "Weather data for mood playlists", "openweather", RequiresKey: true),
        new("ws.audioscrobbler.com", "Last.fm metadata and scrobbling", "lastfm", RequiresKey: true),
        new("www.googleapis.com", "YouTube metadata", "youtube", RequiresKey: true),
        new("img.youtube.com", "YouTube thumbnails", "youtube"),
        new("api.deezer.com", "Fallback metadata", "deezer"),
        new("radio-browser.info", "Radio station directory", "radio"),
        new("somafm.com", "SomaFM radio streams", "radio"),
        new("open.spotify.com", "Spotify page metadata (scraping)", "spotify"),
        new("github.com", "Update checks", null),
        new("ollama.ai", "Local AI model downloads", "ollama"),
        // NEW: No-API-Key sources
        new("bandcamp.com", "Bandcamp audio downloads and metadata", "yt-dlp"),
        new("audiomack.com", "Audiomack audio downloads and metadata", "yt-dlp"),
        new("archive.org", "Internet Archive audio downloads", "yt-dlp"),
        new("mixcloud.com", "Mixcloud audio downloads", "yt-dlp"),
    };
}