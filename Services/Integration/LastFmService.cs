using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using NullWave.Models;
using Serilog;

namespace NullWave.Services;

public class LastFmService
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly string _apiKey;
    private readonly LastFmAuthService _auth;
    private readonly string _sessionKey;
    private const string BaseUrl = "https://ws.audioscrobbler.com/2.0/";

    public LastFmService(ConfigService config)
    {
        _apiKey     = config.GetLastFmApiKey();
        _sessionKey = config.GetLastFmSessionKey();
        _auth       = new LastFmAuthService(_apiKey, config.GetLastFmApiSecret());
    }

    // Legacy properties for backwards compatibility with MainViewModel
    public bool IsConfigured => !string.IsNullOrEmpty(_apiKey);
    public bool CanScrobble => IsConfigured && !string.IsNullOrEmpty(_sessionKey);

    // Decoupled properties for granular validation (Settings UI vs Enrichment vs Scrobbling)
    public bool IsConfiguredForRead => !string.IsNullOrWhiteSpace(_apiKey);
    public bool IsConfiguredForScrobbling => !string.IsNullOrWhiteSpace(_apiKey) && !string.IsNullOrEmpty(_sessionKey) && _auth.IsConfigured;

    public async Task<(string Title, string Artist)> SearchTrackAsync(
        string title, string artist)
    {
        if (!IsConfiguredForRead)
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

    public async Task<LastFmTrackInfo?> GetTrackInfoAsync(string title, string artist)
    {
        if (!IsConfiguredForRead) return null;

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

            // Use DTO for accurate tag extraction from nested JSON structure
            var responseObj = JsonSerializer.Deserialize<LastFmTrackResponse>(json);
            if (responseObj?.Track?.TopTags?.TagList != null)
            {
                info.Tags = responseObj.Track.TopTags.TagList
                    .Select(t => t.Name)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .Take(5)
                    .ToList();
            }
            else
            {
                // Fallback to JsonDocument if DTO deserialization fails
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
            }

            if (track.TryGetProperty("listeners", out var listeners))
                info.Listeners = listeners.GetString() ?? "0";

            if (track.TryGetProperty("playcount", out var playcount))
                info.GlobalPlayCount = playcount.GetString() ?? "0";

            if (track.TryGetProperty("album", out var album) &&
                album.TryGetProperty("image", out var images))
            {
                foreach (var img in images.EnumerateArray())
                {
                    if (img.TryGetProperty("size", out var size) &&
                        (size.GetString() == "extralarge" || size.GetString() == "large"))
                    {
                        var artUrl = img.GetProperty("#text").GetString();
                        if (!string.IsNullOrEmpty(artUrl))
                        {
                            info.AlbumArtUrl = artUrl;
                            break;
                        }
                    }
                }
            }

            if (track.TryGetProperty("wiki", out var wiki) &&
                wiki.TryGetProperty("summary", out var summary))
            {
                var raw = summary.GetString() ?? string.Empty;
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

    public async Task<bool> ScrobbleAsync(string title, string artist, DateTime playedAt)
    {
        if (!IsConfiguredForScrobbling)
        {
            Log.Debug("[LastFm] Scrobble skipped — not fully configured for write operations");
            return false;
        }

        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artist) ||
            artist.Equals("Unknown Artist", StringComparison.OrdinalIgnoreCase) ||
            artist.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            Log.Debug("[LastFm] Scrobble skipped — missing or unknown artist for '{Title}'", title);
            return false;
        }

        try
        {
            var timestamp = ((DateTimeOffset)playedAt.ToUniversalTime()).ToUnixTimeSeconds().ToString();

            var parameters = new SortedDictionary<string, string>
            {
                ["method"]    = "track.scrobble",
                ["api_key"]   = _apiKey,
                ["sk"]        = _sessionKey,
                ["artist"]    = artist,
                ["track"]     = title,
                ["timestamp"] = timestamp
            };

            var sig = _auth.Sign(parameters);

            var formContent = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("method",     "track.scrobble"),
                new KeyValuePair<string, string>("api_key",   _apiKey),
                new KeyValuePair<string, string>("sk",        _sessionKey),
                new KeyValuePair<string, string>("artist",    artist),
                new KeyValuePair<string, string>("track",     title),
                new KeyValuePair<string, string>("timestamp", timestamp),
                new KeyValuePair<string, string>("api_sig",   sig),
                new KeyValuePair<string, string>("format",    "json")
            });

            var response = await _http.PostAsync(BaseUrl, formContent);
            var responseJson = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Log.Warning("[LastFm] Scrobble failed for '{Title}' by '{Artist}': {Response}",
                    title, artist, responseJson);
                return false;
            }

            Log.Information("[LastFm] Scrobbled: {Title} by {Artist} at {Time}",
                title, artist, playedAt);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[LastFm] Scrobble failed for {Title}", title);
            return false;
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
    public string? AlbumArtUrl { get; set; }
}

// DTOs for accurate Last.fm API JSON mapping
public class LastFmTrackResponse
{
    [System.Text.Json.Serialization.JsonPropertyName("track")]
    public LastFmTrackDetails? Track { get; set; }
}

public class LastFmTrackDetails
{
    [System.Text.Json.Serialization.JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("artist")]
    public LastFmArtistDetails? Artist { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("toptags")]
    public LastFmTopTags? TopTags { get; set; }
}

public class LastFmArtistDetails
{
    [System.Text.Json.Serialization.JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public class LastFmTopTags
{
    [System.Text.Json.Serialization.JsonPropertyName("tag")]
    public System.Collections.Generic.List<LastFmTag>? TagList { get; set; } = new();
}

public class LastFmTag
{
    [System.Text.Json.Serialization.JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}