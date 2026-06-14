using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using NullWave.Helpers;
using NullWave.Models;
using NullWave.Services.Metadata;
using Serilog;

namespace NullWave.Services;

public class MetadataService
{
    private readonly YouTubeMetadataFetcher _youTube;
    private readonly SoundCloudMetadataFetcher _soundCloud;
    private readonly LocalMetadataFetcher _local;
    private readonly LastFmService _lastFm;
    private readonly UrlParserService _urlParser = new();

    public MetadataService(ConfigService config, LastFmService lastFm)
    {
        _youTube    = new YouTubeMetadataFetcher(config.GetYouTubeApiKey());
        _soundCloud = new SoundCloudMetadataFetcher();
        _local      = new LocalMetadataFetcher();
        _lastFm     = lastFm;
    }

    public async Task<(string Title, string Artist, string? ThumbnailPath)> FetchFromUrlAsync(string url)
    {
        var source = SourceDetector.Detect(url);
        return source switch
        {
            TrackSource.YouTube    => await _youTube.FetchAsync(url),
            TrackSource.SoundCloud => await _soundCloud.FetchAsync(url),
            TrackSource.Spotify    => await FetchSpotifyMetadataAsync(url),
            TrackSource.LastFm     => await FetchLastFmUrlAsync(url),
            _                      => ("Unknown Title", "Unknown Artist", null)
        };
    }

    public (string Title, string Artist) FetchFromLocalFile(string filePath)
        => _local.Fetch(filePath);

    private async Task<(string Title, string Artist, string? ThumbnailPath)>
        FetchSpotifyMetadataAsync(string url)
    {
        var id = _urlParser.ExtractSpotifyId(url);
        if (string.IsNullOrEmpty(id))
            return ("Spotify track (unknown id)", "Unknown", null);

        Log.Warning("Spotify API not available — falling back to Last.fm search");
        if (_lastFm.IsConfigured)
        {
            var (t, a) = await _lastFm.SearchTrackAsync("Unknown", "Unknown");
            return (t, a, null);
        }

        return ($"Spotify track ({id})", "Unknown", null);
    }

    private async Task<(string Title, string Artist, string? ThumbnailPath)>
        FetchLastFmUrlAsync(string url)
    {
        var extracted = _urlParser.ExtractLastFmTrack(url);
        if (extracted == null)
            return ("Last.fm track (unknown)", "Unknown", null);

        var (title, artist) = extracted.Value;
        Log.Information("Last.fm URL parsed: {Title} by {Artist}", title, artist);

        if (_lastFm.IsConfigured)
        {
            var (t, a) = await _lastFm.SearchTrackAsync(title, artist);
            return (t, a, null);
        }

        return (title, artist, null);
    }

    public string? ExtractAlbumArt(string filePath)
    {
        try
        {
            using var file = TagLib.File.Create(filePath);
            if (file.Tag.Pictures == null || file.Tag.Pictures.Length == 0)
                return null;

            var picture = file.Tag.Pictures[0];
            if (picture.Data == null || picture.Data.Count == 0)
                return null;

            var hash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(filePath)))[..16];

            var artPath = Path.Combine(NullWavePaths.ArtCacheDir, $"{hash}.jpg");

            if (!System.IO.File.Exists(artPath))
            {
                System.IO.File.WriteAllBytes(artPath, picture.Data.Data);
                Log.Information("Album art extracted: {Path}", artPath);
            }

            return artPath;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Album art extraction failed for {Path}", filePath);
            return null;
        }
    }
}