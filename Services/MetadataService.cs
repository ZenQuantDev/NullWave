using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using NullWave.Helpers;
using NullWave.Models;
using Serilog;
using TagLib;

namespace NullWave.Services;

public class MetadataService
{
    private readonly HttpClient _http = new();
    private readonly UrlParserService _urlParser = new();
    private readonly LastFmService _lastFm;
    private readonly string _youTubeApiKey;

    public MetadataService(ConfigService config, LastFmService lastFm)
    {
        _youTubeApiKey = config.GetYouTubeApiKey();
        _lastFm = lastFm;
    }

    public async Task<(string Title, string Artist)> FetchFromUrlAsync(string url)
    {
        var source = SourceDetector.Detect(url);
        return source switch
        {
            TrackSource.YouTube => await FetchYouTubeMetadataAsync(url),
            TrackSource.Spotify => await FetchSpotifyMetadataAsync(url),
            TrackSource.SoundCloud => await FetchSoundCloudMetadataAsync(url),
            TrackSource.LastFm => await FetchLastFmUrlAsync(url),
            _ => ("Unknown Title", "Unknown Artist")
        };
    }

    // ── YouTube ──────────────────────────────────────────────────────────────

    private async Task<(string Title, string Artist)> FetchYouTubeMetadataAsync(string url)
    {
        var id = _urlParser.ExtractYouTubeId(url);
        if (string.IsNullOrEmpty(id))
            return ("YouTube track (unknown id)", "Unknown");

        if (string.IsNullOrEmpty(_youTubeApiKey))
        {
            Log.Warning("YouTube API key not configured");
            return ($"YouTube track ({id})", "Unknown");
        }

        try
        {
            var requestUrl = $"https://www.googleapis.com/youtube/v3/videos" +
                             $"?part=snippet&id={id}&key={_youTubeApiKey}";

            var response = await _http.GetAsync(requestUrl);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            var items = doc.RootElement.GetProperty("items");
            if (items.GetArrayLength() == 0)
                return ("Unknown Title", "Unknown Artist");

            var snippet = items[0].GetProperty("snippet");
            var title = snippet.GetProperty("title").GetString() ?? "Unknown Title";
            var artist = snippet.GetProperty("channelTitle").GetString() ?? "Unknown Artist";

            Log.Information("YouTube metadata fetched: {Title} by {Artist}", title, artist);

            // Enrich with Last.fm if configured
            if (_lastFm.IsConfigured)
            {
                var enriched = await _lastFm.SearchTrackAsync(title, artist);
                if (enriched.Title != title || enriched.Artist != artist)
                {
                    Log.Debug("Last.fm enriched: {Title} by {Artist}",
                        enriched.Title, enriched.Artist);
                    return enriched;
                }
            }

            return (title, artist);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "YouTube metadata fetch failed for {Url}", url);
            return ("Unknown Title", "Unknown Artist");
        }
    }

    // ── Spotify ──────────────────────────────────────────────────────────────

    private async Task<(string Title, string Artist)> FetchSpotifyMetadataAsync(string url)
    {
        var id = _urlParser.ExtractSpotifyId(url);
        if (string.IsNullOrEmpty(id))
            return ("Spotify track (unknown id)", "Unknown");

        // No Spotify API access — try Last.fm search as fallback
        Log.Warning("Spotify API not available — falling back to Last.fm search");

        if (_lastFm.IsConfigured)
            return await _lastFm.SearchTrackAsync("Unknown", "Unknown");

        return ($"Spotify track ({id})", "Unknown");
    }

    // ── SoundCloud ────────────────────────────────────────────────────────────

    private async Task<(string Title, string Artist)> FetchSoundCloudMetadataAsync(string url)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(
                "yt-dlp",
                $"--no-download --print \"%(title)s\" --print \"%(uploader)s\" \"{url}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true
            };

            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null) return ("SoundCloud track", "Unknown");

            var output = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();

            var lines = output.Split('\n',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries);

            var title  = lines.Length > 0 ? lines[0] : "SoundCloud track";
            var artist = lines.Length > 1 ? lines[1] : "Unknown";

            Log.Information("SoundCloud metadata fetched: {Title} by {Artist}", title, artist);
            return (title, artist);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SoundCloud metadata fetch failed for {Url}", url);
            return ("SoundCloud track", "Unknown");
        }
    }

    // ── Last.fm ───────────────────────────────────────────────────────────

    private async Task<(string Title, string Artist)> FetchLastFmUrlAsync(string url)
    {
        var extracted = _urlParser.ExtractLastFmTrack(url);
        if (extracted == null)
            return ("Last.fm track (unknown)", "Unknown");

        var (title, artist) = extracted.Value;
        Log.Information("Last.fm URL parsed: {Title} by {Artist}", title, artist);

        // Enrich with full track info if API key configured
        if (_lastFm.IsConfigured)
        {
            var enriched = await _lastFm.SearchTrackAsync(title, artist);
            return enriched;
        }

        return (title, artist);
    }

    // ── Local Files ──────────────────────────────────────────────────────────

    public (string Title, string Artist) FetchFromLocalFile(string filePath)
    {
        try
        {
            using var file = TagLib.File.Create(filePath);
            var title = file.Tag.Title;
            var artist = file.Tag.FirstPerformer
                         ?? (file.Tag.Performers.Length > 0
                             ? string.Join(", ", file.Tag.Performers)
                             : null);

            // Fall back to filename if tags are empty
            if (string.IsNullOrWhiteSpace(title))
                title = System.IO.Path.GetFileNameWithoutExtension(filePath);
            if (string.IsNullOrWhiteSpace(artist))
                artist = "Unknown";

            Log.Information("Local file tags read: {Title} by {Artist}", title, artist);
            return (title, artist);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "TagLib failed for {Path}, falling back to filename", filePath);
            return (System.IO.Path.GetFileNameWithoutExtension(filePath), "Unknown");
        }
    }
    public string? ExtractAlbumArt(string filePath)
    {
        try
        {
            using var file = TagLib.File.Create(filePath);
            if (file.Tag.Pictures == null || file.Tag.Pictures.Length == 0)
                return null;

            var picture = file.Tag.Pictures[0];
            if (picture.Data == null || picture.Data.Count == 0)
                return null;

            // Build cache path: ~/.nullwave/art/{hash}.jpg
            var hash = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(filePath)))
                [..16];

            var artPath = System.IO.Path.Combine(NullWavePaths.ArtCacheDir, $"{hash}.jpg");

            if (!System.IO.File.Exists(artPath))
            {
                System.IO.File.WriteAllBytes(artPath, picture.Data.Data);
                Log.Information("Album art extracted: {Path}", artPath);
            }

            return artPath;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Album art extraction failed for {Path}", filePath);
            return null;
        }
    }
}