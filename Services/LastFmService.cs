using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Serilog;

namespace NullWave.Services;

public class LastFmService
{
    private readonly HttpClient _http = new();
    private readonly string _apiKey;
    private const string BaseUrl = "https://ws.audioscrobbler.com/2.0/";

    public LastFmService(ConfigService config)
    {
        _apiKey = config.GetLastFmApiKey();
    }

    public bool IsConfigured => !string.IsNullOrEmpty(_apiKey);

    // Search for a track — returns corrected title + artist if found
    public async Task<(string Title, string Artist)> SearchTrackAsync(
        string title, string artist)
    {
        if (!IsConfigured)
        {
            Log.Warning("Last.fm API key not configured");
            return (title, artist);
        }

        try
        {
            var url = $"{BaseUrl}?method=track.search" +
                      $"&track={Uri.EscapeDataString(title)}" +
                      $"&artist={Uri.EscapeDataString(artist)}" +
                      $"&api_key={_apiKey}&format=json&limit=1";

            var response = await _http.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            var matches = doc.RootElement
                .GetProperty("results")
                .GetProperty("trackmatches")
                .GetProperty("track");

            if (matches.ValueKind == JsonValueKind.Array &&
                matches.GetArrayLength() > 0)
            {
                var first = matches[0];
                var foundTitle = first.GetProperty("name").GetString() ?? title;
                var foundArtist = first.GetProperty("artist").GetString() ?? artist;
                Log.Debug("Last.fm search: {Title} by {Artist}", foundTitle, foundArtist);
                return (foundTitle, foundArtist);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Last.fm search failed for {Title} by {Artist}", title, artist);
        }

        return (title, artist);
    }

    // Get detailed track info — tags, listeners, wiki summary
    public async Task<LastFmTrackInfo?> GetTrackInfoAsync(string title, string artist)
    {
        if (!IsConfigured) return null;

        try
        {
            var url = $"{BaseUrl}?method=track.getInfo" +
                      $"&track={Uri.EscapeDataString(title)}" +
                      $"&artist={Uri.EscapeDataString(artist)}" +
                      $"&api_key={_apiKey}&format=json";

            var response = await _http.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("track", out var track))
                return null;

            var info = new LastFmTrackInfo
            {
                Title = track.GetProperty("name").GetString() ?? title,
                Artist = track.GetProperty("artist")
                    .GetProperty("name").GetString() ?? artist,
            };

            // Listeners
            if (track.TryGetProperty("listeners", out var listeners))
                info.Listeners = listeners.GetString() ?? "0";

            // Play count
            if (track.TryGetProperty("playcount", out var playcount))
                info.GlobalPlayCount = playcount.GetString() ?? "0";

            // Tags
            if (track.TryGetProperty("toptags", out var toptags) &&
                toptags.TryGetProperty("tag", out var tags))
            {
                foreach (var tag in tags.EnumerateArray())
                {
                    var tagName = tag.GetProperty("name").GetString();
                    if (!string.IsNullOrEmpty(tagName))
                        info.Tags.Add(tagName);
                    if (info.Tags.Count >= 5) break;
                }
            }

            // Wiki summary
            if (track.TryGetProperty("wiki", out var wiki) &&
                wiki.TryGetProperty("summary", out var summary))
            {
                var raw = summary.GetString() ?? string.Empty;
                // Strip Last.fm's appended HTML link
                var cutoff = raw.IndexOf("<a href", StringComparison.OrdinalIgnoreCase);
                info.WikiSummary = cutoff > 0
                    ? raw[..cutoff].Trim()
                    : raw.Trim();
            }

            Log.Debug("Last.fm track info fetched: {Title} by {Artist}", info.Title, info.Artist);
            return info;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Last.fm getInfo failed for {Title} by {Artist}", title, artist);
            return null;
        }
    }
}

public class LastFmTrackInfo
{
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string Listeners { get; set; } = "0";
    public string GlobalPlayCount { get; set; } = "0";
    public System.Collections.Generic.List<string> Tags { get; set; } = new();
    public string? WikiSummary { get; set; }
}