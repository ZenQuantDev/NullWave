using System;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Serilog;

namespace NullWave.Services.Metadata;

public partial class YouTubeMetadataFetcher
{
    private readonly HttpClient _http = new();
    private readonly string _apiKey;

    public YouTubeMetadataFetcher(string apiKey)
    {
        _apiKey = apiKey;
    }

    public async Task<(string Title, string Artist, string? ThumbnailPath)> FetchAsync(string url)
    {
        // Fail gracefully instead of throwing a 400 Bad Request exception
        if (url.Contains("list=") || url.Contains("/playlist?"))
        {
            Log.Warning("Skipped single-track metadata fetch for playlist URL: {Url}", url);
            // Return empty strings instead of "Unknown" to prevent UI flicker
            return (string.Empty, string.Empty, null); 
        }

        // Normalize youtu.be shortlinks to standard watch URLs for the API
        if (url.Contains("youtu.be/"))
        {
            try
            {
                var uri = new Uri(url);
                var videoId = uri.AbsolutePath.TrimStart('/');
                if (!string.IsNullOrEmpty(videoId))
                {
                    url = $"https://www.youtube.com/watch?v={videoId}";
                }
            }
            catch (UriFormatException)
            {
                // Ignore and fall through to ExtractYouTubeId
            }
        }

        var id = ExtractYouTubeId(url);
        if (string.IsNullOrEmpty(id))
            return ("YouTube track (unknown id)", "Unknown", null);

        string? thumbnailPath = await FetchThumbnailAsync(id);

        if (string.IsNullOrEmpty(_apiKey))
        {
            Log.Warning("YouTube API key not configured");
            return ($"YouTube track ({id})", "Unknown", thumbnailPath);
        }

        try
        {
            var requestUrl =
                $"https://www.googleapis.com/youtube/v3/videos" +
                $"?part=snippet&id={id}&key={_apiKey}";

            var response = await _http.GetAsync(requestUrl);
            
            // FIX: Manual status check to avoid throwing exceptions on expected API rejections (400, 403, 404)
            if (!response.IsSuccessStatusCode)
            {
                Log.Warning("YouTube API returned {StatusCode} for {Url}", response.StatusCode, url);
                return ("Unknown Title", "Unknown Artist", thumbnailPath);
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var items = doc.RootElement.GetProperty("items");

            if (items.GetArrayLength() == 0)
                return ("Unknown Title", "Unknown Artist", thumbnailPath);

            var snippet = items[0].GetProperty("snippet");
            var title   = snippet.GetProperty("title").GetString()        ?? "Unknown Title";
            var artist  = snippet.GetProperty("channelTitle").GetString() ?? "Unknown Artist";

            Log.Information("YouTube metadata fetched: {Title} by {Artist}", title, artist);
            return (title, artist, thumbnailPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "YouTube metadata fetch failed for {Url}", url);
            return ("Unknown Title", "Unknown Artist", thumbnailPath);
        }
    }

    public static async Task<string?> FetchThumbnailAsync(string videoId)
    {
        var thumbUrl = $"https://img.youtube.com/vi/{videoId}/mqdefault.jpg";
        return await ThumbnailDownloader.FetchAsync(thumbUrl, $"yt_{videoId}");
    }

    [GeneratedRegex(@"(?:youtube\.com\/watch\?v=|youtu\.be\/)([^&\s\?#]+)", RegexOptions.IgnoreCase)]
    private static partial Regex YouTubeIdRegex();

    public static string? ExtractYouTubeId(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var match = YouTubeIdRegex().Match(url);
        return match.Success ? match.Groups[1].Value : null;
    }
}