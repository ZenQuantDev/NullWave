using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NullWave.Models;
using Serilog;

namespace NullWave.Services.Metadata;

public static partial class TrackTitleParser
{
    [GeneratedRegex(@"\s+(ft\.?|feat\.?)\s+.+$", RegexOptions.IgnoreCase)]
    private static partial Regex FeatureRegex();

    [GeneratedRegex(@"^(.+?)\s*(?:\/\/\/|\/\/|⧸|⧵|~|∞|·|•)\s*(.+)$", RegexOptions.Compiled)]
    private static partial Regex ExoticSeparatorRegex();

    // FIX: Added em dash
    private static readonly string[] Separators = { " - ", " – ", " — " };

    // FIX: Centralized placeholder detection for metadata reconciliation
    private static readonly HashSet<string> PlaceholderTitles = new(StringComparer.OrdinalIgnoreCase)
    {
        "Unknown Title",
        "YouTube track",
        "SoundCloud track",
        "Spotify track",
        "Radio track",
        "Audiobook track"
    };

    /// <summary>True if the title is a known placeholder that should be overwritten by real metadata.</summary>
    public static bool IsPlaceholderTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return true;
        if (PlaceholderTitles.Contains(title)) return true;
        // Also treat raw URLs as placeholders
        if (title.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            title.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public static bool IsPlaceholderArtist(string? artist)
    {
        if (string.IsNullOrWhiteSpace(artist)) return true;
        return artist.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
            || artist.Equals("Unknown Artist", StringComparison.OrdinalIgnoreCase);
    }

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

        // FIX: Replaced JunkPatterns loop with TitleSanitizer
        title = TitleSanitizer.SanitizeSingle(title);

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

        // FIX: Replaced ClutterRegex with TitleSanitizer
        var cleaned = TitleSanitizer.SanitizeSingle(rawTitle);
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