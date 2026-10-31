using System;
using System.IO;
using System.Linq;
using System.Net.Http;
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
    private readonly YtDlpMetadataFetcher _ytDlpFallback = new("YouTube");
    
    // NEW: Generic fetchers for yt-dlp-supported sources
    private readonly YtDlpMetadataFetcher _bandcamp;
    private readonly YtDlpMetadataFetcher _audiomack;
    private readonly YtDlpMetadataFetcher _archiveOrg;
    private readonly YtDlpMetadataFetcher _mixcloud;

    public MetadataService(ConfigService config, LastFmService lastFm)
    {
        _youTube    = new YouTubeMetadataFetcher(config.GetYouTubeApiKey());
        _soundCloud = new SoundCloudMetadataFetcher();
        _local      = new LocalMetadataFetcher();
        _lastFm     = lastFm;
        
        // NEW: Initialize generic fetchers
        _bandcamp   = new YtDlpMetadataFetcher("Bandcamp");
        _audiomack  = new YtDlpMetadataFetcher("Audiomack");
        _archiveOrg = new YtDlpMetadataFetcher("Archive.org");
        _mixcloud   = new YtDlpMetadataFetcher("Mixcloud");
    }

    public async Task<(string Title, string Artist, string? ThumbnailPath, TimeSpan Duration)> FetchFromUrlAsync(string url)
    {
        var source = SourceDetector.Detect(url);
        return source switch
        {
            TrackSource.YouTube    => await FetchYouTubeWithFallbackAsync(url),
            TrackSource.SoundCloud => await _soundCloud.FetchAsync(url),
            TrackSource.Spotify    => await FetchSpotifyMetadataAsync(url),
            TrackSource.LastFm     => await FetchLastFmUrlAsync(url),
            // NEW: Route to generic yt-dlp fetcher
            TrackSource.Bandcamp   => await _bandcamp.FetchAsync(url),
            TrackSource.Audiomack  => await _audiomack.FetchAsync(url),
            TrackSource.ArchiveOrg => await _archiveOrg.FetchAsync(url),
            TrackSource.Mixcloud   => await _mixcloud.FetchAsync(url),
            _                      => ("Unknown Title", "Unknown Artist", null, TimeSpan.Zero)
        };
    }

    private async Task<(string Title, string Artist, string? ThumbnailPath, TimeSpan Duration)> FetchYouTubeWithFallbackAsync(string url)
    {
        try 
        {
            var result = await _youTube.FetchAsync(url);
            // If the API returns placeholders or fails, use our yt-dlp fallback
            if (result.Title == "Unknown Title" || string.IsNullOrWhiteSpace(result.Title))
            {
                Log.Information("[MetadataService] YouTube API returned no data, falling back to yt-dlp for {Url}", url);
                var fb = await _ytDlpFallback.FetchAsync(url);
                // Keep the API's thumbnail if yt-dlp didn't find one
                return (fb.Title, fb.Artist, fb.ThumbnailPath ?? result.ThumbnailPath, fb.Duration);
            }
            return result;
        }
        catch (Exception ex)
        {
            // FIX: Removed reference to out-of-scope 'result' variable
            Log.Warning(ex, "[MetadataService] YouTube API threw, falling back to yt-dlp for {Url}", url);
            return await _ytDlpFallback.FetchAsync(url);
        }
    }

    public (string? Album, int TrackNumber) FetchAlbumAndTrackNumber(string filePath)
        => _local.FetchAlbumAndTrackNumber(filePath);

    public MediaType DetectLocalMediaType(string filePath)
        => _local.DetectMediaType(filePath);

    public (string Title, string Artist, TimeSpan Duration) FetchFromLocalFile(string filePath)
        => _local.Fetch(filePath);

    private static readonly HttpClient _sharedHttp = new() { Timeout = TimeSpan.FromSeconds(10) };

    private async Task<(string Title, string Artist, string? ThumbnailPath, TimeSpan Duration)> FetchSpotifyMetadataAsync(string url)
    {
        try
        {
            var html = await _sharedHttp.GetStringAsync(SpotifyPageParser.CleanUrl(url));
            var page = SpotifyPageParser.Parse(html);

            if (page.Kind is SpotifyPageKind.Album or SpotifyPageKind.Playlist)
            {
                Log.Warning("[MetadataService] Spotify {Kind} links are not supported as single tracks.", page.Kind);
                return ("Unknown Title", "Unknown Artist", null, TimeSpan.Zero);
            }

            return (page.Title, page.Artist, null, TimeSpan.FromSeconds(page.DurationSeconds));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[MetadataService] Spotify page fetch failed for {Url}", url);
            return ("Spotify track", "Unknown", null, TimeSpan.Zero);
        }
    }

    private async Task<(string Title, string Artist, string? ThumbnailPath, TimeSpan Duration)> FetchLastFmUrlAsync(string url)
    {
        var extracted = _urlParser.ExtractLastFmTrack(url);
        if (extracted == null) return ("Last.fm track (unknown)", "Unknown", null, TimeSpan.Zero);

        var (title, artist) = extracted.Value;
        Log.Information("Last.fm URL parsed: {Title} by {Artist}", title, artist);

        if (_lastFm.IsConfigured)
        {
            var (t, a) = await _lastFm.SearchTrackAsync(title, artist);
            return (t, a, null, TimeSpan.Zero);
        }
        return (title, artist, null, TimeSpan.Zero);
    }

    public async Task<string?> ExtractAlbumArtAsync(string filePath)
    {
        try
        {
            return await Task.Run(() =>
            {
                using var file = TagLib.File.Create(filePath);
                if (file.Tag.Pictures == null || file.Tag.Pictures.Length == 0) return null;

                var picture = file.Tag.Pictures[0];
                if (picture.Data == null || picture.Data.Count == 0) return null;

                var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(filePath)))[..16];
                var artPath = Path.Combine(NullWavePaths.ArtCacheDir, $"{hash}.jpg");

                if (!System.IO.File.Exists(artPath))
                {
                    System.IO.File.WriteAllBytes(artPath, picture.Data.Data);
                    Log.Information("Album art extracted: {Path}", artPath);
                }
                return artPath;
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Album art extraction failed for {Path}", filePath);
            return null;
        }
    }

    public bool WriteTagsToFile(string filePath, string? title, string? artist)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return false;
        try
        {
            using var file = TagLib.File.Create(filePath);
            if (file == null) return false;

            var tag = file.Tag;
            var fileTitle = tag.Title ?? "";
            var fileArtist = string.Join(", ", tag.Performers ?? Array.Empty<string>());

            if (NullWave.Helpers.TrackNormalizer.LooselyMatch(title ?? "", fileTitle, artist ?? "", fileArtist)) return false;

            if (NullWave.Helpers.TrackNormalizer.LooselyMatch(title ?? "", fileArtist, artist ?? "", fileTitle))
            {
                Log.Warning("[MetadataService] Refusing to write swapped tags to {Path}", filePath);
                return false;
            }

            tag.Title = title;
            tag.Performers = NullWave.Helpers.TrackNormalizer.SplitArtistCredits(artist ?? "").ToArray();
            file.Save();
            Log.Information("[MetadataService] Wrote embedded tags to file: {Path}", filePath);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[MetadataService] Failed to write tags to {Path}", filePath);
            return false;
        }
    }
}