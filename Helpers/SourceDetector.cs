using NullWave.Models;

namespace NullWave.Helpers;

public static class SourceDetector
{
    public static TrackSource Detect(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return TrackSource.Unknown;
        if (url.Contains("youtube.com") || url.Contains("youtu.be") || url.Contains("music.youtube.com"))
            return TrackSource.YouTube;
        if (url.Contains("spotify.com") || url.Contains("open.spotify.com"))
            return TrackSource.Spotify;
        if (url.Contains("soundcloud.com"))
            return TrackSource.SoundCloud;
        return TrackSource.Unknown;
    }
}