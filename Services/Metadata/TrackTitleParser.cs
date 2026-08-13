using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NullWave.Models;
using Serilog;

namespace NullWave.Services.Metadata;

/// <summary>
/// Unified utility for resolving clean (Title, Artist) search terms and 
/// sanitizing messy YouTube-style metadata strings.
/// </summary>
public static partial class TrackTitleParser
{
    [GeneratedRegex(@"\s*[\(\[](official\s*(music\s*)?video|official\s*audio|lyrics?|hd|hq|visualizer|director'?s?\s*cut)[\)\]]\s*", RegexOptions.IgnoreCase)]
    private static partial Regex ClutterRegex();

    [GeneratedRegex(@"\s+(ft\.?|feat\.?)\s+.+$", RegexOptions.IgnoreCase)]
    private static partial Regex FeatureRegex();

    // Exotic separators used by synthwave / label channels
    [GeneratedRegex(@"^(.+?)\s*(?:\/\/\/|\/\/|⧸|∞|~|〜|·|•)\s*(.+)$", RegexOptions.Compiled)]
    private static partial Regex ExoticSeparatorRegex();

    private static readonly string[] Separators = { " - ", " – ", " — " };
    private static readonly string[] JunkPatterns = { "- Topic", "[Official Music Video]", "(Official Video)", "[Official Video]", "(Video)", "Official Audio" };

    /// <summary>
    /// Checks if a title contains exotic separators that the old parser didn't understand.
    /// </summary>
    public static bool HasExoticSeparator(string s) =>
        s.Contains('~') || s.Contains('∞') || s.Contains('·') || s.Contains('•') || s.Contains('⧸') || s.Contains("///");

    /// <summary>
    /// Picks the best available title/artist pair for a Last.fm search.
    /// </summary>
    public static (string Title, string Artist) ResolveSearchTerms(Track track)
    {
        if (!string.IsNullOrWhiteSpace(track.Artist) && track.Artist != "Unknown" && track.Artist != "Unknown Artist")
            return (track.Title, track.Artist);

        var parsed = TryParseArtistTitle(track.Title);
        if (parsed != null)
            return (parsed.Value.Title, parsed.Value.Artist);

        return (track.Title, string.Empty);
    }

    /// <summary>
    /// Comprehensive sanitization pipeline for raw YouTube/SC metadata.
    /// </summary>
    public static (string CleanArtist, string CleanTitle) CleanYouTubeMetadata(string rawTitle, string rawArtist)
    {
        string title = rawTitle ?? string.Empty;
        string artist = rawArtist ?? string.Empty;

        foreach (var pattern in JunkPatterns)
        {
            title = title.Replace(pattern, "", StringComparison.OrdinalIgnoreCase);
        }

        if (artist.EndsWith("Music", StringComparison.OrdinalIgnoreCase) && artist.Length > 5) 
            artist = artist[..^5];
        if (artist.EndsWith("VEVO", StringComparison.OrdinalIgnoreCase) && artist.Length > 4) 
            artist = artist[..^4];
        if (artist.EndsWith("- Topic", StringComparison.OrdinalIgnoreCase) && artist.Length > 7)
            artist = artist[..^7];

        var parsed = TryParseArtistTitle(title);
        if (parsed != null)
        {
            if (string.IsNullOrWhiteSpace(artist) || artist == "Unknown" || artist == "Unknown Artist")
            {
                artist = parsed.Value.Artist;
            }
            title = parsed.Value.Title;
        }

        return (artist.Trim(), title.Trim());
    }

    /// <summary>
    /// Strips common YouTube title clutter, then splits on exotic or classic separators.
    /// </summary>
    public static (string Artist, string Title)? TryParseArtistTitle(string rawTitle)
    {
        if (string.IsNullOrWhiteSpace(rawTitle)) return null;

        var cleaned = ClutterRegex().Replace(rawTitle, " ");
        cleaned = FeatureRegex().Replace(cleaned, string.Empty);
        cleaned = cleaned.Trim();

        // 1. Try exotic separators first (e.g., PASTEL GHOST ~ POSSESSION)
        var exoticMatch = ExoticSeparatorRegex().Match(cleaned);
        if (exoticMatch.Success)
        {
            var artist = exoticMatch.Groups[1].Value.Trim();
            var title = exoticMatch.Groups[2].Value.Trim();
            if (artist.Length > 1 && title.Length > 1)
                return (artist, title);
        }

        // 2. Fallback to classic dash separators
        foreach (var sep in Separators)
        {
            var idx = cleaned.IndexOf(sep, StringComparison.Ordinal);
            if (idx <= 0) continue;

            var artist = cleaned[..idx].Trim();
            var title  = cleaned[(idx + sep.Length)..].Trim();

            if (artist.Length > 0 && title.Length > 0)
                return (artist, title);
        }

        return null;
    }
}