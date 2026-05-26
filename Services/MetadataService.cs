using System;
using System.Net.Http;
using System.Threading.Tasks;
using NullWave.Models;

namespace NullWave.Services;

public class MetadataService
{
    private readonly HttpClient _http = new();
    private readonly UrlParserService _urlParser = new();

    // Placeholder — will call YouTube / Last.fm APIs later
    public async Task<(string Title, string Artist)> FetchFromUrlAsync(string url)
    {
        await Task.Delay(0); // placeholder for async API call

        var source = Helpers.SourceDetector.Detect(url);

        return source switch
        {
            TrackSource.YouTube => await FetchYouTubeTitleAsync(url),
            TrackSource.Spotify => ("Spotify track (API coming soon)", "Unknown"),
            _ => ("Unknown Title", "Unknown Artist")
        };
    }

    private async Task<(string Title, string Artist)> FetchYouTubeTitleAsync(string url)
    {
        // TODO: plug in YouTube Data API v3 key here later
        // For now returns placeholder
        await Task.Delay(0);
        var id = _urlParser.ExtractYouTubeId(url);
        return ($"YouTube track ({id ?? "unknown"})", "Unknown");
    }

    public (string Title, string Artist) FetchFromLocalFile(string filePath)
    {
        var fileName = System.IO.Path.GetFileNameWithoutExtension(filePath);
        // TODO: use TagLib# later for proper ID3 tag reading
        return (fileName, "Unknown");
    }
}