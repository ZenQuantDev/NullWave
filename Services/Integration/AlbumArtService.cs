using System;
using System.Threading.Tasks;
using NullWave.Models;
using NullWave.Services.Metadata;
using Serilog;

namespace NullWave.Services.Integration;

public class AlbumArtService
{
    private readonly LastFmService _lastFm;

    public const string PlaceholderPath = "avares://NullWave/Assets/placeholder-art.png";

    public AlbumArtService(LastFmService lastFm)
    {
        _lastFm = lastFm;
    }

    public async Task<string> GetArtPathAsync(Track track)
    {
        if (!string.IsNullOrEmpty(track.AlbumArtPath))
            return track.AlbumArtPath;

        string? resolved = track.Source switch
        {
            TrackSource.YouTube    => await TryYouTubeAsync(track),
            TrackSource.SoundCloud => await TrySoundCloudAsync(track),
            _                      => null
        };

        resolved ??= await TryLastFmAsync(track);

        if (resolved != null)
        {
            Log.Information("[AlbumArtService] Resolved art for {Title} via fallback chain", track.Title);
            return resolved;
        }

        Log.Debug("[AlbumArtService] No art found for {Title}, using placeholder", track.Title);
        return PlaceholderPath;
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
            Log.Verbose("[AlbumArtService] No match for Artist={Artist} | Title={Title}. Retrying art resolution with reversed fields.", searchArtist, searchTitle);
            info = await _lastFm.GetTrackInfoAsync(searchArtist, searchTitle);
        }

        if (info == null || string.IsNullOrEmpty(info.AlbumArtUrl)) return null;

        return await ThumbnailDownloader.FetchAsync(info.AlbumArtUrl, $"lfm_{track.Id:N}");
    }
}