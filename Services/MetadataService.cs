using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using NullWave.Helpers;
using NullWave.Models;
using Serilog;

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
        // SoundCloud API registrations are closed — graceful placeholder
        Log.Warning("SoundCloud API not available — registrations currently closed");
        await Task.CompletedTask;

        // Extract a best-guess title from the URL path
        // e.g. soundcloud.com/artist-name/track-name → "track name" by "artist name"
        try
        {
            var uri = new Uri(url);
            var segments = uri.AbsolutePath.Trim('/').Split('/');
            if (segments.Length >= 2)
            {
                var artist = segments[0].Replace("-", " ");
                var title = segments[1].Replace("-", " ");

                // Title-case them
                artist = System.Globalization.CultureInfo.CurrentCulture
                    .TextInfo.ToTitleCase(artist);
                title = System.Globalization.CultureInfo.CurrentCulture
                    .TextInfo.ToTitleCase(title);

                return (title, artist);
            }
        }
        catch { }

        return ("SoundCloud track", "Unknown");
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
        var fileName = System.IO.Path.GetFileNameWithoutExtension(filePath);
        // TODO: TagLib# for proper ID3 tag reading (Phase 3)
        return (fileName, "Unknown");
    }
}