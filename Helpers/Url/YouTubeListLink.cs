using System;

namespace NullWave.Helpers;

public enum YouTubeListKind { None, Mix, Playlist, Private }

public static class YouTubeListLink
{
    public static YouTubeListKind Classify(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u))
            return YouTubeListKind.None;
        var host = u.Host.ToLowerInvariant();
        var isShort = host == "youtu.be";
        if (!isShort && host != "youtube.com" && !host.EndsWith(".youtube.com")) return YouTubeListKind.None;

        var q = System.Web.HttpUtility.ParseQueryString(u.Query);
        var list = q["list"];
        if (string.IsNullOrEmpty(list)) return YouTubeListKind.None;

        var hasVideo = isShort
            ? u.AbsolutePath.Length > 1
            : u.AbsolutePath.Equals("/watch", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(q["v"]);
        if (!hasVideo) return YouTubeListKind.None;

        if (list is "LL" or "WL") return YouTubeListKind.Private;
        if (list.StartsWith("RD", StringComparison.Ordinal) || q["start_radio"] == "1") return YouTubeListKind.Mix;
        return YouTubeListKind.Playlist;
    }
}