using System;
using System.Text.RegularExpressions;
using NullWave.Models;

namespace NullWave.Services.Metadata;

/// <summary>
/// Unified utility for resolving clean (Title, Artist) search terms and 
/// sanitizing messy YouTube-style metadata strings. Consolidates logic 
/// previously split between TrackTitleParser and TrackSanitizer.
/// </summary>
public static partial class TrackTitleParser
{
    [GeneratedRegex(@"\s*[\(\[](official\s*(music\s*)?video|official\s*audio|lyrics?|hd|hq|visualizer|director'?s?\s*cut)[\)\]]\s*", RegexOptions.IgnoreCase)]
    private static partial Regex ClutterRegex();

    [GeneratedRegex(@"\s+(ft\.?|feat\.?)\s+.+$", RegexOptions.IgnoreCase)]
    private static partial Regex FeatureRegex();

    private static readonly string[] Separators = { " - ", " – ", " — " };
    private static readonly string[] JunkPatterns = { "- Topic", "[Official Music Video]", "(Official Video)", "[Official Video]", "(Video)", "Official Audio" };

    /// <summary>
    /// Picks the best available title/artist pair for a Last.fm search.
    /// If Artist is missing/Unknown, attempts to split a messy YouTube-style
    /// title into a clean (Title, Artist) pair before falling back to the
    /// raw title with an empty artist.
    /// </summary>
    public static (string Title, string Artist) ResolveSearchTerms(Track track)
    {
        if (!string.IsNullOrWhiteSpace(track.Artist) && track.Artist != "Unknown")
            return (track.Title, track.Artist);

        var parsed = TryParseArtistTitle(track.Title);
        if (parsed != null)
            return (parsed.Value.Title, parsed.Value.Artist);

        return (track.Title, string.Empty);
    }

    /// <summary>
    /// Comprehensive sanitization pipeline for raw YouTube/SC metadata.
    /// Strips junk suffixes, cleans distributor names, and attempts to 
    /// split "Artist - Title" formats packed into a single string.
    /// </summary>
    public static (string CleanArtist, string CleanTitle) CleanYouTubeMetadata(string rawTitle, string rawArtist)
    {
        string title = rawTitle ?? string.Empty;
        string artist = rawArtist ?? string.Empty;

        // Strip common YouTube junk suffixes from the title
        foreach (var pattern in JunkPatterns)
        {
            title = title.Replace(pattern, "", StringComparison.OrdinalIgnoreCase);
        }

        // Clean up messy distributor artist names
        if (artist.EndsWith("Music", StringComparison.OrdinalIgnoreCase) && artist.Length > 5) 
            artist = artist[..^5];
        if (artist.EndsWith("VEVO", StringComparison.OrdinalIgnoreCase) && artist.Length > 4) 
            artist = artist[..^4];
        if (artist.EndsWith("- Topic", StringComparison.OrdinalIgnoreCase) && artist.Length > 7)
            artist = artist[..^7];

        // Attempt to parse "Artist - Title" if it's still packed in the title
        var parsed = TryParseArtistTitle(title);
        if (parsed != null)
        {
            if (string.IsNullOrWhiteSpace(artist) || artist == "Unknown")
            {
                artist = parsed.Value.Artist;
            }
            title = parsed.Value.Title;
        }

        return (artist.Trim(), title.Trim());
    }

    /// <summary>
    /// Strips common YouTube title clutter using compiled Regex, then splits 
    /// on the first valid separator into (Artist, Title). 
    /// Returns null if no separator is found.
    /// </summary>
    public static (string Artist, string Title)? TryParseArtistTitle(string rawTitle)
    {
        if (string.IsNullOrWhiteSpace(rawTitle)) return null;

        var cleaned = ClutterRegex().Replace(rawTitle, " ");
        cleaned = FeatureRegex().Replace(cleaned, string.Empty);
        cleaned = cleaned.Trim();

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