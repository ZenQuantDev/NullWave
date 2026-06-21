using System;
using System.Text.RegularExpressions;
using NullWave.Models;

namespace NullWave.Services.Metadata;

/// <summary>
/// Shared helper for resolving clean (Title, Artist) search terms from a
/// Track whose Artist is missing/Unknown but whose Title is a messy
/// YouTube-style string like "Mariah Carey - Obsessed (Official Music Video)".
///
/// Extracted from AlbumArtService so LastFmEnrichmentService can use the
/// exact same parsing logic for tag lookups — previously this lived only
/// in AlbumArtService, so EnrichTrackAsync sent unparsed messy titles
/// straight to Last.fm and got no tags back for most mainstream tracks
/// imported from YouTube with no separate artist field.
/// </summary>
public static class TrackTitleParser
{
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
    /// Strips common YouTube title clutter — "(Official Video)", "(Lyrics)",
    /// "ft. X", trailing tags — then splits on the first " - " separator
    /// into (Artist, Title). Returns null if no separator is found.
    /// </summary>
    public static (string Artist, string Title)? TryParseArtistTitle(string rawTitle)
    {
        if (string.IsNullOrWhiteSpace(rawTitle)) return null;

        var cleaned = Regex.Replace(
            rawTitle,
            @"\s*[\(\[](official\s*(music\s*)?video|official\s*audio|lyrics?|hd|hq|visualizer|director'?s?\s*cut)[\)\]]\s*",
            " ",
            RegexOptions.IgnoreCase);

        cleaned = Regex.Replace(
            cleaned, @"\s+(ft\.?|feat\.?)\s+.+$", string.Empty,
            RegexOptions.IgnoreCase);

        cleaned = cleaned.Trim();

        var separators = new[] { " - ", " – ", " — " };
        foreach (var sep in separators)
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