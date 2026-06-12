using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Serilog;

namespace NullWave.Services.Metadata;

public class YouTubeMetadataFetcher
{
    private readonly HttpClient _http = new();
    private readonly string _apiKey;

    public YouTubeMetadataFetcher(string apiKey)
    {
        _apiKey = apiKey;
    }

    public async Task<(string Title, string Artist)> FetchAsync(string url)
    {
        var id = ExtractYouTubeId(url);
        if (string.IsNullOrEmpty(id))
            return ("YouTube track (unknown id)", "Unknown");

        if (string.IsNullOrEmpty(_apiKey))
        {
            Log.Warning("YouTube API key not configured");
            return ($"YouTube track ({id})", "Unknown");
        }

        try
        {
            var requestUrl = $"https://www.googleapis.com/youtube/v3/videos?part=snippet&id={id}&key={_apiKey}";
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
            return (title, artist);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "YouTube metadata fetch failed for {Url}", url);
            return ("Unknown Title", "Unknown Artist");
        }
    }

    private static string? ExtractYouTubeId(string url)
    {
        if (!url.Contains("youtube.com") && !url.Contains("youtu.be")) return null;
        if (url.Contains("youtu.be/"))
            return url.Split("youtu.be/")[1].Split('?')[0];
        if (url.Contains("v="))
            return url.Split("v=")[1].Split('&')[0];
        return null;
    }
}