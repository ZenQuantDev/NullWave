using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using NullWave.Helpers;
using Serilog;

namespace NullWave.Services;

public class SpotifyBridgeResult
{
    public string SpotifyTitle  { get; init; } = string.Empty;
    public string SpotifyArtist { get; init; } = string.Empty;
    public string YouTubeUrl    { get; init; } = string.Empty;
    public string YouTubeTitle  { get; init; } = string.Empty;
    public bool   Found         { get; init; }
}

public class SpotifyBridgeService
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly ConfigService _config;

    static SpotifyBridgeService()
    {
        _http.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
    }

    public SpotifyBridgeService(ConfigService config)
    {
        _config = config;
    }

    public async Task<SpotifyBridgeResult> BridgeAsync(string spotifyUrl)
    {
        var (title, artist) = await FetchSpotifyMetaAsync(spotifyUrl);
        if (string.IsNullOrEmpty(title))
        {
            Log.Warning("[SpotifyBridge] Could not extract metadata from {Url}", spotifyUrl);
            return new SpotifyBridgeResult { Found = false };
        }

        Log.Information("[SpotifyBridge] Spotify metadata: {Title} by {Artist}", title, artist);

        var (ytUrl, ytTitle) = await SearchYouTubeAsync(title, artist);
        if (string.IsNullOrEmpty(ytUrl))
        {
            Log.Warning("[SpotifyBridge] No YouTube match for {Title} by {Artist}", title, artist);
            return new SpotifyBridgeResult { SpotifyTitle = title, SpotifyArtist = artist, Found = false };
        }

        Log.Information("[SpotifyBridge] YouTube match: {YtTitle} → {YtUrl}", ytTitle, ytUrl);
        return new SpotifyBridgeResult { SpotifyTitle = title, SpotifyArtist = artist, YouTubeUrl = ytUrl, YouTubeTitle = ytTitle, Found = true };
    }

    private async Task<(string Title, string Artist)> FetchSpotifyMetaAsync(string spotifyUrl)
    {
        try
        {
            var cleanUrl = SpotifyPageParser.CleanUrl(spotifyUrl);
            var html = await _http.GetStringAsync(cleanUrl);
            var page = SpotifyPageParser.Parse(html);

            if (page.Kind is SpotifyPageKind.Album or SpotifyPageKind.Playlist)
            {
                Log.Warning("[SpotifyBridge] {Kind} links are not supported yet: {Url}", page.Kind, spotifyUrl);
                return (string.Empty, string.Empty);
            }

            return (page.Title, page.Artist);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[SpotifyBridge] Page fetch failed for {Url}", spotifyUrl);
            return (string.Empty, string.Empty);
        }
    }

    private async Task<(string Url, string Title)> SearchYouTubeAsync(string title, string artist)
    {
        var ytDlpResult = await SearchViaYtDlpAsync(title, artist);
        if (!string.IsNullOrEmpty(ytDlpResult.Url)) return ytDlpResult;

        var apiKey = _config.GetYouTubeApiKey();
        if (!string.IsNullOrEmpty(apiKey))
        {
            var apiResult = await SearchViaApiAsync(title, artist, apiKey);
            if (!string.IsNullOrEmpty(apiResult.Url)) return apiResult;
        }

        return (string.Empty, string.Empty);
    }

    private async Task<(string Url, string Title)> SearchViaApiAsync(string title, string artist, string apiKey)
    {
        try
        {
            var query = Uri.EscapeDataString($"{title} {artist} official audio");
            var url   = $"https://www.googleapis.com/youtube/v3/search?part=snippet&q={query}&type=video&maxResults=1&key={apiKey}";
            var json = await _http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            var items = doc.RootElement.GetProperty("items");
            if (items.GetArrayLength() == 0) return (string.Empty, string.Empty);

            var item     = items[0];
            var videoId  = item.GetProperty("id").GetProperty("videoId").GetString() ?? string.Empty;
            var vidTitle = item.GetProperty("snippet").GetProperty("title").GetString() ?? string.Empty;
            return ($"https://www.youtube.com/watch?v={videoId}", vidTitle);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[SpotifyBridge] YouTube API search failed");
            return (string.Empty, string.Empty);
        }
    }

    private static async Task<(string Url, string Title)> SearchViaYtDlpAsync(string title, string artist)
    {
        try
        {
            var query = $"ytsearch1:{title} {artist} official audio";
            var result = await ProcessRunner.RunAsync(
                "yt-dlp", 
                new[] { "--no-download", "--print", "id", "--print", "title", query },
                timeout: TimeSpan.FromSeconds(15),
                resolveViaPath: true);

            if (result.ExitCode != 0) return (string.Empty, string.Empty);

            var lines = result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (lines.Length >= 2)
                return ($"https://www.youtube.com/watch?v={lines[0]}", lines[1]);

            return (string.Empty, string.Empty);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[SpotifyBridge] yt-dlp search fallback failed");
            return (string.Empty, string.Empty);
        }
    }
}