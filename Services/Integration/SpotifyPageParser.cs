using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace NullWave.Services;

public enum SpotifyPageKind { Unknown, Track, Album, Playlist }

public sealed record SpotifyPageInfo(
    SpotifyPageKind Kind,
    string Title,
    string Artist,
    int DurationSeconds,
    IReadOnlyList<string> TrackUrls);

/// <summary>
/// Reads the public link-preview tags of open.spotify.com pages. No API key, best effort:
/// Spotify can change these tags at any time, so keep this class small and covered by tests.
/// </summary>
public static class SpotifyPageParser
{
    private static readonly Regex MetaTag =
        new(@"<meta\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex KeyAttr =
        new(@"\b(?:property|name)\s*=\s*""([^""]*)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ContentAttr =
        new(@"\bcontent\s*=\s*""([^""]*)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Album pages use "Name - Single by Artist | Spotify" as og:title.
    private static readonly Regex TitleSuffix = new(
        @"^(?<name>.+?)\s+-\s+(?:Single|Album|EP|Compilation|Playlist)\s+by\s+.+?\|\s*Spotify\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Drops the ?si=... tracking part so the request is a clean page URL.</summary>
    public static string CleanUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.GetLeftPart(UriPartial.Path) : url;

    public static SpotifyPageInfo Parse(string html)
    {
        var tags = new List<KeyValuePair<string, string>>();
        foreach (Match tag in MetaTag.Matches(html ?? string.Empty))
        {
            var key = KeyAttr.Match(tag.Value);
            var val = ContentAttr.Match(tag.Value);
            if (key.Success && val.Success)
                tags.Add(new(key.Groups[1].Value, WebUtility.HtmlDecode(val.Groups[1].Value).Trim()));
        }

        string First(string key) =>
            tags.FirstOrDefault(t => t.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value
            ?? string.Empty;

        var kind = First("og:type") switch
        {
            "music.song"     => SpotifyPageKind.Track,
            "music.album"    => SpotifyPageKind.Album,
            "music.playlist" => SpotifyPageKind.Playlist,
            _                => SpotifyPageKind.Unknown
        };

        var title = First("og:title");
        var suffix = TitleSuffix.Match(title);
        if (suffix.Success) title = suffix.Groups["name"].Value.Trim();

        // Track pages carry the artist name directly; otherwise the first "·" segment of the
        // description is the artist ("C418 · single · 2018 · 1 songs"). Playlists have no artist.
        var artist = First("music:musician_description");
        if (artist.Length == 0)
            artist = First("og:description")
                .Split('·', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault() ?? string.Empty;
        if (kind == SpotifyPageKind.Playlist) artist = string.Empty;

        _ = int.TryParse(First("music:duration"), out var seconds);

        var trackUrls = tags
            .Where(t => t.Key.Equals("music:song", StringComparison.OrdinalIgnoreCase))
            .Select(t => t.Value)
            .Where(v => v.Length > 0)
            .Distinct()
            .ToList();

        return new SpotifyPageInfo(kind, title, artist, seconds, trackUrls);
    }
}