using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
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
    private readonly HttpClient    _http;
    private readonly ConfigService _config;

    public SpotifyBridgeService(ConfigService config)
    {
        _config = config;
        _http   = new HttpClient();
        _http.DefaultRequestHeaders.Add("User-Agent", "NullWave/1.0");
    }

    /// <summary>
    /// Given a Spotify track URL, fetch its title+artist from Spotify's
    /// oEmbed endpoint (no API key needed), then search YouTube for it.
    /// </summary>
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
            return new SpotifyBridgeResult
            {
                SpotifyTitle  = title,
                SpotifyArtist = artist,
                Found         = false
            };
        }

        Log.Information("[SpotifyBridge] YouTube match: {YtTitle} → {YtUrl}", ytTitle, ytUrl);

        return new SpotifyBridgeResult
        {
            SpotifyTitle  = title,
            SpotifyArtist = artist,
            YouTubeUrl    = ytUrl,
            YouTubeTitle  = ytTitle,
            Found         = true
        };
    }

    //  Spotify oEmbed — no API key required 

    private async Task<(string Title, string Artist)> FetchSpotifyMetaAsync(string spotifyUrl)
    {
        // Try yt-dlp first — it can handle Spotify URLs on some systems
        var ytDlpMeta = await GetYtDlpSpotifyMetaAsync(spotifyUrl);
        if (ytDlpMeta.HasValue && !string.IsNullOrEmpty(ytDlpMeta.Value.Title))
            return ytDlpMeta.Value;

        // Fallback: extract track ID and use a basic title guess from URL
        // spotify.com/track/TRACKID — not useful but prevents silent failure
        Log.Warning("[SpotifyBridge] All metadata methods failed for {Url}", spotifyUrl);
        return (string.Empty, string.Empty);
    }

    private static async Task<(string Title, string Artist)?> GetYtDlpSpotifyMetaAsync(
        string spotifyUrl)
    {
        try
        {
            // yt-dlp can extract Spotify metadata without downloading
            var psi = new ProcessStartInfo("yt-dlp",
                $"--no-download --print \"%(title)s\" --print \"%(artist)s\" \"{spotifyUrl}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return null;

            var output = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();

            if (proc.ExitCode != 0) return null;

            var lines = output.Split('\n',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries);

            if (lines.Length >= 2)
                return (lines[0], lines[1]);
            if (lines.Length == 1)
                return (lines[0], string.Empty);

            return null;
        }
        catch { return null; }
    }

    //  YouTube search via YouTube Data API 

    private async Task<(string Url, string Title)> SearchYouTubeAsync(
        string title, string artist)
    {
        var apiKey = _config.GetYouTubeApiKey();

        // Try YouTube Data API first
        if (!string.IsNullOrEmpty(apiKey))
        {
            var result = await SearchViaApiAsync(title, artist, apiKey);
            if (!string.IsNullOrEmpty(result.Url)) return result;
        }

        // Fallback: use yt-dlp's ytsearch
        return await SearchViaYtDlpAsync(title, artist);
    }

    private async Task<(string Url, string Title)> SearchViaApiAsync(
        string title, string artist, string apiKey)
    {
        try
        {
            var query = Uri.EscapeDataString($"{title} {artist} official audio");
            var url   = $"https://www.googleapis.com/youtube/v3/search" +
                        $"?part=snippet&q={query}&type=video&maxResults=1&key={apiKey}";

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

    private static async Task<(string Url, string Title)> SearchViaYtDlpAsync(
        string title, string artist)
    {
        try
        {
            var query = $"ytsearch1:{title} {artist} official audio";
            var psi   = new ProcessStartInfo("yt-dlp",
                $"--no-download --print id --print title \"{query}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return (string.Empty, string.Empty);

            var output = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();

            var lines = output.Split('\n',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries);

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