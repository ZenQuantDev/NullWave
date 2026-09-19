using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using NullWave.Helpers;
using Serilog;

namespace NullWave.Services;

public class RadioBrowserProvider
{
    private static readonly HttpClient _http = CreateHttpClient();
    private const string BaseUrl = "https://de1.api.radio-browser.info/json";

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.Add("User-Agent", "NullWave/0.6.0");
        return client;
    }

    public async Task<List<RadioChannel>> SearchAsync(string query, int limit = 20)
    {
        try
        {
            var url = $"{BaseUrl}/stations/search?name={Uri.EscapeDataString(query)}&limit={limit}&hidebroken=true";
            var json = await _http.GetStringAsync(url);
            return ParseStations(json);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[RadioBrowserProvider] Search failed for {Query}", query);
            return new List<RadioChannel>();
        }
    }

    public async Task<List<RadioChannel>> BrowseByGenreAsync(string genre, int limit = 20)
    {
        try
        {
            var url = $"{BaseUrl}/stations/bytag/{Uri.EscapeDataString(genre)}?limit={limit}&hidebroken=true&order=clickcount&reverse=true";
            var json = await _http.GetStringAsync(url);
            return ParseStations(json);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[RadioBrowserProvider] Genre browse failed for {Genre}", genre);
            return new List<RadioChannel>();
        }
    }

    public async Task<List<RadioChannel>> GetTopStationsAsync(int limit = 20)
    {
        try
        {
            var json = await _http.GetStringAsync($"{BaseUrl}/stations/topclick/{limit}?hidebroken=true");
            return ParseStations(json);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[RadioBrowserProvider] Top stations fetch failed");
            return new List<RadioChannel>();
        }
    }

    private List<RadioChannel> ParseStations(string json)
    {
        var results = new List<RadioChannel>();
        using var doc = JsonDocument.Parse(json);
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var name = item.TryGetProperty("name", out var n) ? n.GetString() : null;
            var url = item.TryGetProperty("url_resolved", out var u) ? u.GetString() : null;
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url)) continue;
            results.Add(new RadioChannel(name.Trim(), url.Trim()));
        }
        return results;
    }
}
