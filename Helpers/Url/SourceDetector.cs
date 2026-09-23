using System;
using System.Text.RegularExpressions;
using NullWave.Models;

namespace NullWave.Helpers;

public static class SourceDetector
{
    // Matches hosts pasted without a scheme, e.g. "youtu.be/abc" or "soundcloud.com/a/b".
    private static readonly Regex BareHost = new(
        @"^[a-z0-9-]+(\.[a-z0-9-]+)*\.[a-z]{2,}(/|$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Detects the source from the URL's host, not from any substring of the text.</summary>
    public static TrackSource Detect(string url)
    {
        var host = GetHost(url);
        if (host is null) return TrackSource.Unknown;

        if (HostIs(host, "youtube.com") || HostIs(host, "youtu.be")) return TrackSource.YouTube;
        if (HostIs(host, "spotify.com"))                              return TrackSource.Spotify;
        if (HostIs(host, "soundcloud.com") || HostIs(host, "snd.sc")) return TrackSource.SoundCloud;
        if (HostIs(host, "last.fm"))                                  return TrackSource.LastFm;

        return TrackSource.Unknown;
    }

    /// <summary>
    /// Returns true only if the URL is a valid, playable media URL.
    /// Rejects bare domain roots like https://www.youtube.com/
    /// </summary>
    public static bool IsPlayableUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        url = url.Trim();

        var source = Detect(url);

        // Host plus path segments without the scheme: ["soundcloud.com", "c418", "axolotl"]
        var parts = Regex.Replace(url, "^https?://", "", RegexOptions.IgnoreCase)
                         .TrimEnd('/')
                         .Split('/');

        var shortHost = parts[0].StartsWith("on.", StringComparison.OrdinalIgnoreCase) ||
                        parts[0].Equals("snd.sc", StringComparison.OrdinalIgnoreCase);

        return source switch
        {
            TrackSource.YouTube =>
                url.Contains("v=") || url.Contains("youtu.be/") || url.Contains("list=") ||
                url.Contains("/shorts/") || url.Contains("/live/") || url.Contains("/embed/"),
            // Share links (on.soundcloud.com/xyz) have one segment; normal tracks have artist/track.
            TrackSource.SoundCloud =>
                parts.Length >= (shortHost ? 2 : 3),
            TrackSource.Spotify =>
                url.Contains("/track/") || url.Contains("/album/") || url.Contains("/playlist/"),
            TrackSource.LastFm =>
                url.Contains("/music/"),
            TrackSource.Unknown =>
                // Accept non-HTTP local paths, or validate HTTP URLs properly
                !url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ||
                Uri.TryCreate(url, UriKind.Absolute, out _),
            _ => true
        };
    }

    private static string? GetHost(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        var text = url.Trim();
        if (!text.Contains("://") && BareHost.IsMatch(text))
            text = "https://" + text;

        // Local paths parse as file URIs (or fail), so the scheme check keeps them Unknown.
        return Uri.TryCreate(text, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? uri.Host.ToLowerInvariant()
            : null;
    }

    private static bool HostIs(string host, string domain) =>
        host == domain || host.EndsWith("." + domain, StringComparison.Ordinal);
}