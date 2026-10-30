using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NullWave.Models;

namespace NullWave.Helpers;

public static class SourceDetector
{
    private static readonly Regex BareHost = new(
        @"^[a-z0-9-]+(\.[a-z0-9-]+)*\.[a-z]{2,}(/|$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex MarkdownLink = new(@"\]\((https?://[^)\s]+)\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly HashSet<string> MixcloudReservedUsers = new(StringComparer.OrdinalIgnoreCase)
        { "discover", "search", "live", "categories", "select", "tag" };
        
    private static readonly HashSet<string> MixcloudReservedPages = new(StringComparer.OrdinalIgnoreCase)
        { "uploads", "favorites", "listens", "playlists", "stream", "followers", "following", "reposts", "activity" };

    /// <summary>
    /// Cleans up pasted markdown links, angle brackets, and adds https:// to known bare hosts.
    /// </summary>
    public static string NormalizeUrl(string? input)
    {
        var text = (input ?? "").Trim();
        var md = MarkdownLink.Match(text);
        if (md.Success) text = md.Groups[1].Value;
        
        text = text.Trim('<', '>', '"', '\'');
        
        if (!text.Contains("://") && Detect(text) is not (TrackSource.Unknown or TrackSource.Local))
            text = "https://" + text;
            
        return text;
    }

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
                url.Contains("/track/", StringComparison.OrdinalIgnoreCase),
            TrackSource.Audiomack =>
                url.Contains("/song/", StringComparison.OrdinalIgnoreCase),
            TrackSource.ArchiveOrg =>
                url.Contains("/details/", StringComparison.OrdinalIgnoreCase),
            TrackSource.Mixcloud =>
                parts.Length >= 3 &&
                !MixcloudReservedUsers.Contains(parts[1]) &&
                !MixcloudReservedPages.Contains(parts[2].Split('?', '#')[0]),
            // FIXED: Reject text that isn't a valid HTTP URL or local path
            TrackSource.Unknown =>
                (Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps))
                || Regex.IsMatch(url, @"^([A-Za-z]:[\\/]|/|\\\\)"),
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