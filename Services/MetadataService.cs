using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using NullWave.Models;
using NullWave.Helpers;
using Serilog;

namespace NullWave.Services;

public class MetadataService
{
    private readonly HttpClient _http = new();
    private readonly UrlParserService _urlParser = new();
    private readonly string _youTubeApiKey;

    public MetadataService(ConfigService config)
    {
        _youTubeApiKey = config.GetYouTubeApiKey();
    }

    public async Task<(string Title, string Artist)> FetchFromUrlAsync(string url)
    {
        var source = SourceDetector.Detect(url);
        return source switch
        {
            TrackSource.YouTube => await FetchYouTubeMetadataAsync(url),
            TrackSource.Spotify => ("Spotify track (API coming soon)", "Unknown"),
            TrackSource.SoundCloud => ("SoundCloud track (API coming soon)", "Unknown"),
            _ => ("Unknown Title", "Unknown Artist")
        };
    }

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
            return (title, artist);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "YouTube metadata fetch failed for {Url}", url);
            return ("Unknown Title", "Unknown Artist");
        }
    }

    public (string Title, string Artist) FetchFromLocalFile(string filePath)
    {
        var fileName = System.IO.Path.GetFileNameWithoutExtension(filePath);
        // TODO: TagLib# for proper ID3 tag reading (Phase 3)
        return (fileName, "Unknown");
    }
}