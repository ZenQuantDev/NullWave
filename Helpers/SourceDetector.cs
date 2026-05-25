using NullWave.Models;

namespace NullWave.Helpers;

public static class SourceDetector
{
    public static TrackSource Detect(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return TrackSource.Unknown;

        if (url.Contains("youtube.com") || url.Contains("youtu.be"))
            return TrackSource.YouTube;
        if (url.Contains("spotify.com"))
            return TrackSource.Spotify;
        if (url.Contains("instagram.com"))
            return TrackSource.Instagram;

        return TrackSource.Unknown;
    }
}