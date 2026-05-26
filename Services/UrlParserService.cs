using System;
using NullWave.Models;

namespace NullWave.Services;

public class UrlParserService
{
    public bool IsValidUrl(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var result)
            && (result.Scheme == Uri.UriSchemeHttp || result.Scheme == Uri.UriSchemeHttps);
    }

    public string? ExtractYouTubeId(string url)
    {
        if (!url.Contains("youtube.com") && !url.Contains("youtu.be")) return null;

        if (url.Contains("youtu.be/"))
            return url.Split("youtu.be/")[1].Split('?')[0];

        if (url.Contains("v="))
            return url.Split("v=")[1].Split('&')[0];

        return null;
    }

    public string? ExtractSpotifyId(string url)
    {
        if (!url.Contains("spotify.com/track/")) return null;
        return url.Split("spotify.com/track/")[1].Split('?')[0];
    }

    public bool IsLocalFile(string path)
    {
        return System.IO.File.Exists(path);
    }

    public string? GetFileExtension(string path)
    {
        return System.IO.Path.GetExtension(path)?.ToLower();
    }

    public bool IsSupportedAudioFile(string path)
    {
        var ext = GetFileExtension(path);
        return ext is ".mp3" or ".flac" or ".wav" or ".ogg" or ".m4a" or ".aac";
    }
}