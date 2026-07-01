using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Serilog;

namespace NullWave.Services;

public class LastFmSessionResult
{
    public bool   Success    { get; init; }
    public string SessionKey { get; init; } = string.Empty;
    public string Username   { get; init; } = string.Empty;
    public string? Error     { get; init; }
}

/// <summary>
/// Implements Last.fm's "desktop application" authentication flow.
/// </summary>
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

    public bool IsConfigured =>
        !string.IsNullOrEmpty(_apiKey) && !string.IsNullOrEmpty(_apiSecret);

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
            var url = $"{BaseUrl}?method=auth.getToken&api_key={_apiKey}" +
                      $"&api_sig={sig}&format=json";

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

    public string GetAuthUrl(string token) =>
        $"{AuthUrl}?api_key={_apiKey}&token={token}";

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
            var url = $"{BaseUrl}?method=auth.getSession&api_key={_apiKey}" +
                      $"&token={token}&api_sig={sig}&format=json";

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