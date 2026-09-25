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

    public MetadataService(ConfigService config, LastFmService lastFm)
    {
        _youTube    = new YouTubeMetadataFetcher(config.GetYouTubeApiKey());
        _soundCloud = new SoundCloudMetadataFetcher();
        _local      = new LocalMetadataFetcher();
        _lastFm     = lastFm;
    }

    public async Task<(string Title, string Artist, string? ThumbnailPath, TimeSpan Duration)> FetchFromUrlAsync(string url)
    {
        var source = SourceDetector.Detect(url);
        return source switch
        {
            TrackSource.YouTube    => await _youTube.FetchAsync(url),
            TrackSource.SoundCloud => await _soundCloud.FetchAsync(url),
            TrackSource.Spotify    => await FetchSpotifyMetadataAsync(url),
            TrackSource.LastFm     => await FetchLastFmUrlAsync(url),
            _                      => ("Unknown Title", "Unknown Artist", null, TimeSpan.Zero)
        };
    }

    public (string? Album, int TrackNumber) FetchAlbumAndTrackNumber(string filePath)
        => _local.FetchAlbumAndTrackNumber(filePath);

    public MediaType DetectLocalMediaType(string filePath)
        => _local.DetectMediaType(filePath);

    public (string Title, string Artist, TimeSpan Duration) FetchFromLocalFile(string filePath)
        => _local.Fetch(filePath);

    private async Task<(string Title, string Artist, string? ThumbnailPath, TimeSpan Duration)> FetchSpotifyMetadataAsync(string url)
    {
        try
        {
            using var http = new HttpClient();
            var html = await http.GetStringAsync(SpotifyPageParser.CleanUrl(url));
            var page = SpotifyPageParser.Parse(html);

            // Guard against Album/Playlist links (C11)
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

    public string? ExtractAlbumArt(string filePath)
    {
        try
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

            if (LibraryService.TitlesLooselyMatch(title ?? "", fileTitle, artist ?? "", fileArtist)) return false;

            if (LibraryService.TitlesLooselyMatch(title ?? "", fileArtist, artist ?? "", fileTitle))
            {
                Log.Warning("[MetadataService] Refusing to write swapped tags to {Path}", filePath);
                return false;
            }

            tag.Title = title;
            tag.Performers = LibraryService.SplitArtistCredits(artist ?? "").ToArray();
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