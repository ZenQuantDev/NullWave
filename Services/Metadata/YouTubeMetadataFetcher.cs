using System;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using Serilog;

namespace NullWave.Services.Metadata;

public partial class YouTubeMetadataFetcher
{
    private readonly HttpClient _http = new();
    private readonly string _apiKey;

    public YouTubeMetadataFetcher(string apiKey) { _apiKey = apiKey; }

    public async Task<(string Title, string Artist, string? ThumbnailPath, TimeSpan Duration)> FetchAsync(string url)
    {
        if (url.Contains("list=") || url.Contains("/playlist?"))
        {
            Log.Warning("Skipped single-track metadata fetch for playlist URL: {Url}", url);
            return (string.Empty, string.Empty, null, TimeSpan.Zero);
        }

        if (url.Contains("youtu.be/"))
        {
            try
            {
                var uri = new Uri(url);
                var videoId = uri.AbsolutePath.TrimStart('/');
                if (!string.IsNullOrEmpty(videoId)) url = $"https://www.youtube.com/watch?v={videoId}";
            }
            catch (UriFormatException) { }
        }

        var id = ExtractYouTubeId(url);
        if (string.IsNullOrEmpty(id)) return ($"YouTube track ({id})", "Unknown", null, TimeSpan.Zero);

        string? thumbnailPath = await FetchThumbnailAsync(id);

        if (string.IsNullOrEmpty(_apiKey))
        {
            Log.Warning("YouTube API key not configured");
            return ($"YouTube track ({id})", "Unknown", thumbnailPath, TimeSpan.Zero);
        }

        try
        {
            var requestUrl = $"https://www.googleapis.com/youtube/v3/videos?part=snippet,contentDetails&id={id}&key={_apiKey}";
            var response = await _http.GetAsync(requestUrl);
            if (!response.IsSuccessStatusCode)
            {
                Log.Warning("YouTube API returned {StatusCode} for {Url}", response.StatusCode, url);
                return ("Unknown Title", "Unknown Artist", thumbnailPath, TimeSpan.Zero);
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var items = doc.RootElement.GetProperty("items");
            if (items.GetArrayLength() == 0) return ("Unknown Title", "Unknown Artist", thumbnailPath, TimeSpan.Zero);

            var snippet = items[0].GetProperty("snippet");
            var title = snippet.GetProperty("title").GetString() ?? "Unknown Title";
            var artist = snippet.GetProperty("channelTitle").GetString() ?? "Unknown Artist";

            var duration = TimeSpan.Zero;
            if (items[0].TryGetProperty("contentDetails", out var contentDetails) &&
                contentDetails.TryGetProperty("duration", out var durationElement))
            {
                var iso = durationElement.GetString();
                if (!string.IsNullOrEmpty(iso))
                {
                    try { duration = XmlConvert.ToTimeSpan(iso); } catch { duration = TimeSpan.Zero; }
                }
            }

            Log.Information("YouTube metadata fetched: {Title} by {Artist}", title, artist);
            return (title, artist, thumbnailPath, duration);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "YouTube metadata fetch failed for {Url}", url);
            return ("Unknown Title", "Unknown Artist", thumbnailPath, TimeSpan.Zero);
        }
    }

    public static async Task<string?> FetchThumbnailAsync(string videoId)
    {
        // Probe from highest quality (1080p) down to standard definition.
        // Includes both WebP and JPG formats. SkiaSharp handles decoding both.
        var urls = new[]
        {
            $"https://i.ytimg.com/vi_webp/{videoId}/maxresdefault.webp", // 1920x1080 WebP
            $"https://img.youtube.com/vi/{videoId}/maxresdefault.jpg",   // 1920x1080 JPG
            $"https://i.ytimg.com/vi_webp/{videoId}/sddefault.webp",     // 640x480 WebP
            $"https://img.youtube.com/vi/{videoId}/sddefault.jpg",       // 640x480 JPG
            $"https://img.youtube.com/vi/{videoId}/hqdefault.jpg",       // 480x360 JPG (has letterbox)
            $"https://img.youtube.com/vi/{videoId}/mqdefault.jpg"        // 320x180 JPG (fallback)
        };

        return await ThumbnailDownloader.FetchWithFallbackAsync(urls, $"yt_{videoId}");
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