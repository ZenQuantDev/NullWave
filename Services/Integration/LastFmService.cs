using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NullWave.Services.Integration;
using Serilog;

namespace NullWave.Services;

public class LastFmSessionResult
{
    public bool   Success    { get; init; }
    public string SessionKey { get; init; } = string.Empty;
    public string Username   { get; init; } = string.Empty;
    public string? Error     { get; init; }
}

public class LastFmAuthService
{
    private const string BaseUrl = "https://ws.audioscrobbler.com/2.0/";
    private const string AuthUrl = "https://www.last.fm/api/auth/";
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly string _apiKey;
    private readonly string _apiSecret;

    public LastFmAuthService(string apiKey, string apiSecret)
    {
        _apiKey    = apiKey;
        _apiSecret = apiSecret;
    }

    public bool IsConfigured => !string.IsNullOrEmpty(_apiKey) && !string.IsNullOrEmpty(_apiSecret);

    public async Task<string?> GetRequestTokenAsync()
    {
        if (!IsConfigured)
        {
            Log.Warning("[LastFmAuth] API key/secret not configured");
            return null;
        }

        try
        {
            var parameters = new SortedDictionary<string, string>
            {
                ["method"]  = "auth.getToken",
                ["api_key"] = _apiKey
            };

            var sig = Sign(parameters);
            var url = $"{BaseUrl}?method=auth.getToken&api_key={_apiKey}&api_sig={sig}&format=json";

            var response = await _http.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("token", out var tokenProp))
            {
                var token = tokenProp.GetString();
                Log.Information("[LastFmAuth] Request token obtained");
                return token;
            }

            Log.Warning("[LastFmAuth] No token in response: {Json}", json);
            return null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[LastFmAuth] Failed to get request token");
            return null;
        }
    }

    public string GetAuthUrl(string token) => $"{AuthUrl}?api_key={_apiKey}&token={token}";

    public async Task<LastFmSessionResult> GetSessionKeyAsync(string token)
    {
        if (!IsConfigured)
            return new LastFmSessionResult { Success = false, Error = "Not configured" };

        try
        {
            var parameters = new SortedDictionary<string, string>
            {
                ["method"]  = "auth.getSession",
                ["api_key"] = _apiKey,
                ["token"]   = token
            };

            var sig = Sign(parameters);
            var url = $"{BaseUrl}?method=auth.getSession&api_key={_apiKey}&token={token}&api_sig={sig}&format=json";

            var response = await _http.GetAsync(url);
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                using var errDoc = JsonDocument.Parse(json);
                var msg = errDoc.RootElement.TryGetProperty("message", out var m)
                    ? m.GetString() : "Authorization not yet granted";
                Log.Warning("[LastFmAuth] Session request failed: {Msg}", msg);
                return new LastFmSessionResult { Success = false, Error = msg };
            }

            using var doc = JsonDocument.Parse(json);
            var session = doc.RootElement.GetProperty("session");
            var sessionKey = session.GetProperty("key").GetString() ?? string.Empty;
            var username   = session.GetProperty("name").GetString() ?? string.Empty;

            Log.Information("[LastFmAuth] Session established for {Username}", username);

            return new LastFmSessionResult
            {
                Success    = true,
                SessionKey = sessionKey,
                Username   = username
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[LastFmAuth] Failed to get session key");
            return new LastFmSessionResult { Success = false, Error = ex.Message };
        }
    }

    public string Sign(SortedDictionary<string, string> parameters)
    {
        var sb = new StringBuilder();
        foreach (var kvp in parameters)
        {
            if (kvp.Key is "format" or "callback") continue;
            sb.Append(kvp.Key).Append(kvp.Value);
        }
        sb.Append(_apiSecret);

        var hash = MD5.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

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

    public bool IsConfigured => !string.IsNullOrEmpty(_apiKey);
    public bool CanScrobble => IsConfigured && !string.IsNullOrEmpty(_sessionKey);
    public bool IsConfiguredForRead => !string.IsNullOrWhiteSpace(_apiKey);
    public bool IsConfiguredForScrobbling => !string.IsNullOrWhiteSpace(_apiKey) && !string.IsNullOrEmpty(_sessionKey) && _auth.IsConfigured;

    public async Task<(string Title, string Artist)> SearchTrackAsync(string title, string artist)
    {
        if (!IsConfiguredForRead) return (title, artist);

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

            var matches = doc.RootElement.GetProperty("results").GetProperty("trackmatches").GetProperty("track");

            if (matches.ValueKind == JsonValueKind.Array && matches.GetArrayLength() > 0)
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
                Artist = track.GetProperty("artist").GetProperty("name").GetString() ?? artist,
            };

            var responseObj = JsonSerializer.Deserialize<LastFmTrackResponse>(json);
            if (responseObj?.Track?.TopTags?.TagList != null)
            {
                info.Tags = responseObj.Track.TopTags.TagList
                    .Select(t => t.Name)
                    .Where(n => !string.IsNullOrEmpty(n) && !global::NullWave.Services.Integration.TagDenylist.IsBlocked(n))
                    .Take(5)
                    .ToList();
            }
            else
            {
                if (track.TryGetProperty("toptags", out var toptags) && toptags.TryGetProperty("tag", out var tags))
                {
                    foreach (var tag in tags.EnumerateArray())
                    {
                        var tagName = tag.GetProperty("name").GetString();
                        if (!string.IsNullOrEmpty(tagName) && !global::NullWave.Services.Integration.TagDenylist.IsBlocked(tagName))
                        {
                            info.Tags.Add(tagName);
                        }
                        if (info.Tags.Count >= 5) break;
                    }
                }
            }

            if (track.TryGetProperty("listeners", out var listeners))
                info.Listeners = listeners.GetString() ?? "0";

            if (track.TryGetProperty("playcount", out var playcount))
                info.GlobalPlayCount = playcount.GetString() ?? "0";

            if (track.TryGetProperty("album", out var album) && album.TryGetProperty("image", out var images))
            {
                foreach (var img in images.EnumerateArray())
                {
                    if (img.TryGetProperty("size", out var size) && (size.GetString() == "extralarge" || size.GetString() == "large"))
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

            if (track.TryGetProperty("wiki", out var wiki) && wiki.TryGetProperty("summary", out var summary))
            {
                var raw = summary.GetString() ?? string.Empty;
                var cutoff = raw.IndexOf("<a href", StringComparison.OrdinalIgnoreCase);
                info.WikiSummary = cutoff > 0 ? raw[..cutoff].Trim() : raw.Trim();
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

    public async Task<LastFmArtistInfo?> GetArtistInfoAsync(string artist)
    {
        if (!IsConfiguredForRead || string.IsNullOrWhiteSpace(artist)) return null;

        try
        {
            var url = $"{BaseUrl}?method=artist.getInfo" +
                      $"&artist={Uri.EscapeDataString(artist)}" +
                      $"&api_key={_apiKey}&format=json";

            var response = await _http.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("artist", out var artistEl))
                return null;

            var info = new LastFmArtistInfo
            {
                Name = artistEl.TryGetProperty("name", out var n) ? n.GetString() ?? artist : artist
            };

            if (artistEl.TryGetProperty("stats", out var stats) && stats.TryGetProperty("listeners", out var listeners))
            {
                info.Listeners = listeners.GetString() ?? "0";
            }

            if (artistEl.TryGetProperty("bio", out var bio) && bio.TryGetProperty("summary", out var summary))
            {
                var raw = summary.GetString() ?? string.Empty;
                var cutoff = raw.IndexOf("<a href", StringComparison.OrdinalIgnoreCase);
                info.Bio = cutoff > 0 ? raw[..cutoff].Trim() : raw.Trim();
            }

            Log.Debug("Last.fm artist info fetched: {Artist}", info.Name);
            return info;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Last.fm artist getInfo failed for {Artist}", artist);
            return null;
        }
    }

    public async Task<bool> ScrobbleAsync(string title, string artist, DateTime playedAt)
    {
        if (!IsConfiguredForScrobbling) return false;

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
                Log.Warning("[LastFm] Scrobble failed for '{Title}' by '{Artist}': {Response}", title, artist, responseJson);
                return false;
            }

            Log.Information("[LastFm] Scrobbled: {Title} by {Artist} at {Time}", title, artist, playedAt);
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

public class LastFmArtistInfo
{
    public string Name { get; set; } = string.Empty;
    public string Listeners { get; set; } = "0";
    public string? Bio { get; set; }
}

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