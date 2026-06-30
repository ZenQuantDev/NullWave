using NullWave.Models;

namespace NullWave.Helpers;

public static class SourceDetector
{
    public static TrackSource Detect(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return TrackSource.Unknown;
        if (url.Contains("youtube.com") || url.Contains("youtu.be") ||
            url.Contains("music.youtube.com"))
            return TrackSource.YouTube;
        if (url.Contains("spotify.com") || url.Contains("open.spotify.com"))
            return TrackSource.Spotify;
        if (url.Contains("soundcloud.com"))
            return TrackSource.SoundCloud;
        if (url.Contains("last.fm"))
            return TrackSource.LastFm;
        return TrackSource.Unknown;
    }

    /// <summary>
    /// Returns true only if the URL is a valid, playable media URL.
    /// Rejects bare domain roots like https://www.youtube.com/
    /// </summary>
    public static bool IsPlayableUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;

        var source = Detect(url);

        return source switch
        {
            TrackSource.YouTube =>
                url.Contains("v=") || url.Contains("youtu.be/") || url.Contains("list="),
            TrackSource.SoundCloud =>
                url.TrimEnd('/').Split('/').Length >= 5,
            TrackSource.Spotify =>
                url.Contains("/track/") || url.Contains("/album/") || url.Contains("/playlist/"),
            TrackSource.LastFm =>
                url.Contains("/music/"),
            TrackSource.Unknown =>
                !url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ||
                url.Length > 30,
            _ => true
        };
    }
}