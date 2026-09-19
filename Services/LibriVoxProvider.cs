using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using NullWave.Helpers;
using Serilog;

namespace NullWave.Services;

public class LibriVoxProvider
{
    private static readonly HttpClient _http = new()
    {
        DefaultRequestHeaders = { { "User-Agent", "NullWave/0.6.0" } }
    };
    private const string BaseUrl = "https://librivox.org/api/feed/audiobooks";

    public async Task<List<RadioChannel>> SearchAsync(string query, int limit = 20)
    {
        try
        {
            var url = $"{BaseUrl}/?title={Uri.EscapeDataString(query)}&format=json";
            var json = await _http.GetStringAsync(url);
            return ParseBooks(json, limit);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[LibriVoxProvider] Search failed for {Query}", query);
            return new List<RadioChannel>();
        }
    }

    public async Task<List<RadioChannel>> GetTopAudiobooksAsync(int limit = 20)
    {
        try
        {
            var url = $"{BaseUrl}/?format=json";
            var json = await _http.GetStringAsync(url);
            return ParseBooks(json, limit);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[LibriVoxProvider] Top audiobooks fetch failed");
            return new List<RadioChannel>();
        }
    }

    private List<RadioChannel> ParseBooks(string json, int limit)
    {
        var results = new List<RadioChannel>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("books", out var books))
            {
                foreach (var item in books.EnumerateArray())
                {
                    if (results.Count >= limit) break;
                    
                    var title = item.TryGetProperty("title", out var t) ? t.GetString() : null;
                    var url = item.TryGetProperty("url_zip_file", out var u) ? u.GetString() : null;
                    
                    if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(url)) continue;
                    results.Add(new RadioChannel(title.Trim(), url.Trim()));
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[LibriVoxProvider] Failed to parse JSON response");
        }
        return results;
    }
}