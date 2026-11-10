using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using NullWave.Plugins.Contracts;

namespace NullWave.Plugins.RadioBrowser;

public class RadioBrowserPlugin : IPlugin
{
    private IPluginHost? _host;
    private HttpClient? _http;
    private string _serverUrl = "https://de1.api.radio-browser.info/json";

    public string Id => "com.nullwave.radiobrowser";
    public string Name => "Radio Browser";
    public string Description => "Browse and import community radio stations from the Radio Browser directory";
    public PluginState State { get; set; } = PluginState.Unavailable;
    public bool IsEnabled { get; set; } = true;

    public async Task<bool> InitializeAsync(IPluginHost host, CancellationToken ct = default)
    {
        _host = host;

        if (!IsEnabled)
        {
            State = PluginState.Disabled;
            host.Log(LogLevel.Info, "Radio Browser plugin disabled by user preference");
            return false;
        }

        try
        {
            _http = new HttpClient();
            _http.DefaultRequestHeaders.Add("User-Agent", "NullWave/0.7.0 (radio-browser plugin)");
            _http.Timeout = TimeSpan.FromSeconds(10);

            // Discover the best server via DNS
            var server = await DiscoverServerAsync(ct);
            if (server != null)
                _serverUrl = $"https://{server}/json";

            // Quick connectivity check
            using var response = await _http.GetAsync($"{_serverUrl}/stats", ct);
            if (!response.IsSuccessStatusCode)
            {
                host.Log(LogLevel.Warning, $"Radio Browser server returned {(int)response.StatusCode}");
                State = PluginState.Unavailable;
                return false;
            }

            State = PluginState.Available;
            host.Log(LogLevel.Info, $"Radio Browser plugin initialized (server: {_serverUrl})");
            return true;
        }
        catch (Exception ex)
        {
            host.Log(LogLevel.Error, $"Radio Browser initialization failed: {ex.Message}");
            State = PluginState.Error;
            return false;
        }
    }

    public Task ShutdownAsync(CancellationToken ct = default)
    {
        _http?.Dispose();
        _http = null;
        State = PluginState.Unavailable;
        _host?.Log(LogLevel.Info, "Radio Browser plugin shut down");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Search for radio stations by name.
    /// </summary>
    public async Task<List<RadioStation>> SearchStationsAsync(
        string query, int limit = 50, CancellationToken ct = default)
    {
        if (_http == null || State != PluginState.Available)
            return new List<RadioStation>();

        try
        {
            var url = $"{_serverUrl}/stations/search?name={Uri.EscapeDataString(query)}&limit={limit}&order=clickcount&reverse=true";
            var json = await _http.GetStringAsync(url, ct);
            var stations = JsonSerializer.Deserialize<List<RadioStationDto>>(json, JsonOptions) ?? new();
            
            var result = new List<RadioStation>();
            foreach (var s in stations)
            {
                if (string.IsNullOrWhiteSpace(s.UrlResolved)) continue;
                result.Add(new RadioStation(
                    s.StationUuid ?? Guid.NewGuid().ToString(),
                    s.Name ?? "Unknown",
                    s.Tags ?? "",
                    s.Country ?? "",
                    s.Language ?? "",
                    s.UrlResolved!,
                    s.Favicon ?? "",
                    s.ClickCount,
                    s.Votes
                ));
            }
            
            _host?.Log(LogLevel.Debug, $"Search returned {result.Count} stations for '{query}'");
            return result;
        }
        catch (Exception ex)
        {
            _host?.Log(LogLevel.Warning, $"Search failed: {ex.Message}");
            return new List<RadioStation>();
        }
    }

    /// <summary>
    /// Get popular stations.
    /// </summary>
    public async Task<List<RadioStation>> GetTopStationsAsync(int limit = 50, CancellationToken ct = default)
    {
        if (_http == null || State != PluginState.Available)
            return new List<RadioStation>();

        try
        {
            var url = $"{_serverUrl}/stations/topclick/{limit}";
            var json = await _http.GetStringAsync(url, ct);
            var stations = JsonSerializer.Deserialize<List<RadioStationDto>>(json, JsonOptions) ?? new();
            
            var result = new List<RadioStation>();
            foreach (var s in stations)
            {
                if (string.IsNullOrWhiteSpace(s.UrlResolved)) continue;
                result.Add(new RadioStation(
                    s.StationUuid ?? Guid.NewGuid().ToString(),
                    s.Name ?? "Unknown",
                    s.Tags ?? "",
                    s.Country ?? "",
                    s.Language ?? "",
                    s.UrlResolved!,
                    s.Favicon ?? "",
                    s.ClickCount,
                    s.Votes
                ));
            }
            return result;
        }
        catch (Exception ex)
        {
            _host?.Log(LogLevel.Warning, $"GetTopStations failed: {ex.Message}");
            return new List<RadioStation>();
        }
    }

    /// <summary>
    /// Get station by UUID and register a click (play event).
    /// </summary>
    public async Task<string?> ResolveStreamUrlAsync(string stationUuid, CancellationToken ct = default)
    {
        if (_http == null || State != PluginState.Available)
            return null;

        try
        {
            // Register click (counts as a "play" on the Radio Browser network)
            var clickUrl = $"{_serverUrl}/url/{stationUuid}";
            await _http.GetAsync(clickUrl, ct);
            
            // Get the actual stream URL
            var urlResponse = await _http.GetStringAsync(clickUrl, ct);
            var dto = JsonSerializer.Deserialize<UrlResponseDto>(urlResponse, JsonOptions);
            return dto?.Url;
        }
        catch (Exception ex)
        {
            _host?.Log(LogLevel.Warning, $"ResolveStreamUrl failed for {stationUuid}: {ex.Message}");
            return null;
        }
    }

    private async Task<string?> DiscoverServerAsync(CancellationToken ct)
    {
        try
        {
            // Radio Browser uses DNS SRV records for server discovery.
            // Fallback: try known servers in order.
            var servers = new[] {
                "de1.api.radio-browser.info",
                "de2.api.radio-browser.info",
                "nl1.api.radio-browser.info",
                "at1.api.radio-browser.info"
            };

            using var testHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            testHttp.DefaultRequestHeaders.Add("User-Agent", "NullWave/0.7.0");

            foreach (var server in servers)
            {
                try
                {
                    var response = await testHttp.GetAsync($"https://{server}/json/stats", ct);
                    if (response.IsSuccessStatusCode)
                        return server;
                }
                catch { /* try next */ }
            }
        }
        catch (Exception ex)
        {
            _host?.Log(LogLevel.Debug, $"Server discovery failed, using default: {ex.Message}");
        }

        return null; // use default
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}

// --- DTOs for Radio Browser API ---

internal class RadioStationDto
{
    [JsonPropertyName("stationuuid")]
    public string? StationUuid { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("url_resolved")]
    public string? UrlResolved { get; set; }

    [JsonPropertyName("tags")]
    public string? Tags { get; set; }

    [JsonPropertyName("country")]
    public string? Country { get; set; }

    [JsonPropertyName("language")]
    public string? Language { get; set; }

    [JsonPropertyName("favicon")]
    public string? Favicon { get; set; }

    [JsonPropertyName("clickcount")]
    public int ClickCount { get; set; }

    [JsonPropertyName("votes")]
    public int Votes { get; set; }
}

internal class UrlResponseDto
{
    [JsonPropertyName("url")]
    public string? Url { get; set; }
}

// --- Public types exposed to consumers ---

public record RadioStation(
    string Id,
    string Name,
    string Genre,
    string Country,
    string Language,
    string StreamUrl,
    string LogoUrl,
    int ClickCount,
    int Votes
);