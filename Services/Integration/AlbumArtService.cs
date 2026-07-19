using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;
using NullWave.Models;
using NullWave.Services.Metadata;
using Serilog;

namespace NullWave.Services.Integration;

public class AlbumArtService
{
    private readonly LastFmService _lastFm;
    private readonly ILogger _logger = Log.ForContext<AlbumArtService>();
    private static readonly ConcurrentDictionary<string, byte> ActiveFetches = new();

    public const string PlaceholderPath = "avares://NullWave/Assets/placeholder-art.png";

    public AlbumArtService(LastFmService lastFm)
    {
        _lastFm = lastFm;
    }

    public async Task<string> GetArtPathAsync(Track track)
    {
        if (!ActiveFetches.TryAdd(track.Id.ToString(), 0))
        {
            _logger.Verbose("Artwork translation already processing for track {TrackId}. Skipping duplicate request pipeline cycle.", track.Id);
            return track.AlbumArtPath ?? PlaceholderPath;
        }

        try
        {
            string? pathOrUrl = track.AlbumArtPath;

            if (string.IsNullOrEmpty(pathOrUrl))
            {
                pathOrUrl = track.Source switch
                {
                    TrackSource.YouTube    => await TryYouTubeAsync(track),
                    TrackSource.SoundCloud => await TrySoundCloudAsync(track),
                    _                      => null
                };

                pathOrUrl ??= await TryLastFmAsync(track);
            }

            if (!string.IsNullOrEmpty(pathOrUrl))
            {
                if (pathOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    pathOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        _logger.Debug("Intercepted remote artwork URL for '{Title}', caching locally...", track.Title);
                        
                        var localPath = await ThumbnailDownloader.FetchAsync(pathOrUrl, $"yt_{track.Id:N}");
                        if (!string.IsNullOrEmpty(localPath))
                        {
                            track.AlbumArtPath = localPath;
                            _logger.Information("Thumbnail saved successfully for track matching: {Title}", track.Title);
                            return localPath;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Failed to download remote thumbnail for '{Title}'", track.Title);
                    }
                }
                else if (File.Exists(pathOrUrl))
                {
                    return pathOrUrl;
                }
            }

            return PlaceholderPath;
        }
        finally
        {
            ActiveFetches.TryRemove(track.Id.ToString(), out _);
        }
    }

    private static async Task<string?> TryYouTubeAsync(Track track)
    {
        if (string.IsNullOrEmpty(track.Url)) return null;
        var id = YouTubeMetadataFetcher.ExtractYouTubeId(track.Url);
        if (string.IsNullOrEmpty(id)) return null;
        return await YouTubeMetadataFetcher.FetchThumbnailAsync(id);
    }

    private static async Task<string?> TrySoundCloudAsync(Track track)
    {
        if (string.IsNullOrEmpty(track.Url)) return null;
        var fetcher = new SoundCloudMetadataFetcher();
        var (_, _, thumbPath) = await fetcher.FetchAsync(track.Url);
        return thumbPath;
    }

    private async Task<string?> TryLastFmAsync(Track track)
    {
        if (!_lastFm.IsConfigured) return null;
        var (searchArtist, searchTitle) = TitleSanitizer.Sanitize(track.Title);
        if (string.IsNullOrWhiteSpace(searchArtist) || searchArtist == "Unknown Artist" || searchArtist == "Unknown")
        {
            var parsed = TrackTitleParser.TryParseArtistTitle(track.Title);
            if (parsed != null)
            {
                searchArtist = parsed.Value.Artist;
                searchTitle = parsed.Value.Title;
            }
        }
        var info = await _lastFm.GetTrackInfoAsync(searchTitle, searchArtist);
        if (info == null && !string.IsNullOrWhiteSpace(searchArtist) && searchArtist != "Unknown Artist" && searchArtist != "Unknown")
        {
            info = await _lastFm.GetTrackInfoAsync(searchArtist, searchTitle);
        }
        if (info == null || string.IsNullOrEmpty(info.AlbumArtUrl)) return null;
        return await ThumbnailDownloader.FetchAsync(info.AlbumArtUrl, $"lfm_{track.Id:N}");
    }
}