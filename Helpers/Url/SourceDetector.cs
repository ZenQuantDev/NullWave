using System;
using System.Linq;
using System.Text.RegularExpressions;
using NullWave.Models;

namespace NullWave.Helpers;

public static class SourceDetector
{
    private static readonly Regex BareHost = new(
        @"^[a-z0-9-]+(\.[a-z0-9-]+)*\.[a-z]{2,}(/|$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static TrackSource Detect(string url)
    {
        var host = GetHost(url);
        if (host is null) return TrackSource.Unknown;

        var site = SupportedSites.FindByHost(host);
        return site?.Source ?? TrackSource.Unknown;
    }

    public static bool IsYtDlpSource(TrackSource source)
    {
        var site = SupportedSites.FindBySource(source);
        return site?.IsYtDlpNative ?? false;
    }

    public static bool IsPlayableUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        url = url.Trim();

        var source = Detect(url);
        var host = GetHost(url) ?? string.Empty;

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
            TrackSource.SoundCloud =>
                parts.Length >= (shortHost ? 2 : 3),
            TrackSource.Spotify =>
                url.Contains("/track/") || url.Contains("/album/") || url.Contains("/playlist/"),
            TrackSource.LastFm =>
                url.Contains("/music/"),
            TrackSource.Bandcamp =>
                url.Contains("/track/", StringComparison.OrdinalIgnoreCase) ||
                url.Contains("/album/", StringComparison.OrdinalIgnoreCase) ||
                (parts.Length >= 3 && parts.Any(p => p.Equals("track", StringComparison.OrdinalIgnoreCase) || p.Equals("album", StringComparison.OrdinalIgnoreCase))),
            TrackSource.Audiomack =>
                url.Contains("/song/", StringComparison.OrdinalIgnoreCase) ||
                url.Contains("/album/", StringComparison.OrdinalIgnoreCase), // Removed /artist/ as it's a profile
            TrackSource.ArchiveOrg =>
                url.Contains("/details/", StringComparison.OrdinalIgnoreCase),
            TrackSource.Mixcloud =>
                parts.Length >= 3 && 
                !parts[1].Equals("uploads", StringComparison.OrdinalIgnoreCase) &&
                !parts[1].Equals("favorites", StringComparison.OrdinalIgnoreCase) &&
                !parts[1].Equals("playlists", StringComparison.OrdinalIgnoreCase),
            TrackSource.Unknown =>
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

        return Uri.TryCreate(text, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? uri.Host.ToLowerInvariant()
            : null;
    }
}