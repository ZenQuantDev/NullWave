using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NullWave.Models;

namespace NullWave.Helpers;

/// <summary>
/// Single source of truth for track identity, title/artist cleaning, and fuzzy matching.
/// Replaces: TitleSanitizer, TrackTitleParser, LibraryService.CleanYouTubeArtifacts,
/// NormalizeArtistKey, SplitArtistCredits, TitlesLooselyMatch, IsDuplicate.
/// </summary>
public static partial class TrackNormalizer
{
    // --- Regex patterns ---
    
    [GeneratedRegex(@"[\(\[\{«「『][^\)\]\}»」』]*?\b(?:official|video|audio|music|lyric|lyrics|visualizer|clip|remastered|remaster|explicit|clean|version|hq|hd|4k|uncensored|edit|download|caption|captions|cc|unreleased|long|cut|mono|stereo|spatial|atmos|remix)s?\b[^\)\]\}»」』]*?[\)\]\}»」』]", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex BracketGarbageRegex();

    [GeneratedRegex(@"\b(official\s+video|official\s+music\s+video|official\s+audio|lyric\s+video|lyrics|official\s+visualizer|unreleased|remastered|explicit\s+version|clean\s+version)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex LooseGarbageRegex();

    [GeneratedRegex(@"[\s,\-\|]+(ft\.?|feat\.?|featuring|with)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex TrailingFeatureRegex();

    [GeneratedRegex(@"\s+(ft\.?|feat\.?)\s+.+$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex FeatureRegex();

    [GeneratedRegex(@"^(.+?)\s*(?:\/\/\/|\/\/|⧸|⧵|~|∞|·|•)\s*(.+)$", RegexOptions.Compiled)]
    private static partial Regex ExoticSeparatorRegex();

    [GeneratedRegex(@"\s*-\s*Topic\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex TopicSuffixRegex();

    [GeneratedRegex(@"\s*(?:,|&|\bfeat\.?\b|\bft\.?\b|\bfeaturing\b)\s*", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ArtistSeparatorRegex();

    [GeneratedRegex(@"\b(?:ft\.?|feat\.?|featuring|with|vs\.?)\s+(.+?)(?=\s*[\(\[\-–-]|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex FeatureArtistRegex();

    [GeneratedRegex(@"[\(\[](.*?)[\)\]]", RegexOptions.Compiled)]
    private static partial Regex BracketContentRegex();

    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"[\s\-\|,•·]+$", RegexOptions.Compiled)]
    private static partial Regex TrailingPunctuationRegex();

    // --- Constants ---
    
    private static readonly string[] Dividers = { " - ", " – ", " — ", " ~ ", " | ", " // ", " ∞ " };
    private static readonly string[] ClassicSeparators = { " - ", " – ", " — " };
    
    private static readonly HashSet<string> PlaceholderTitles = new(StringComparer.OrdinalIgnoreCase)
    {
        "Unknown Title",
        "YouTube track",
        "SoundCloud track",
        "Spotify track",
        "Radio track",
        "Audiobook track"
    };

    private static readonly HashSet<string> DecorationTokens = new(StringComparer.Ordinal)
    {
        "official", "music", "video", "audio", "lyric", "lyrics", "explicit", "clean",
        "version", "radio", "edit", "remix", "remastered", "live", "acoustic",
        "ft", "feat", "featuring", "hd", "hq", "mv", "prod", "produced", "by",
        "and", "with", "of", "in", "on", "part", "pt", "the", "that", "this"
    };


    // CLEANING


    /// <summary>Strips YouTube clutter, bracket garbage, and trailing features from a title.</summary>
    public static string CleanTitle(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var cleaned = FlattenUnicodeFonts(raw);
        return ApplyScrubbingPasses(cleaned);
    }

    /// <summary>Strips Topic/VEVO/Music suffixes from an artist name.</summary>
    public static string CleanArtist(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var artist = raw.Trim();
        
        if (artist.EndsWith("Music", StringComparison.OrdinalIgnoreCase) && artist.Length > 5)
            artist = artist[..^5];
        if (artist.EndsWith("VEVO", StringComparison.OrdinalIgnoreCase) && artist.Length > 4)
            artist = artist[..^4];
        
        artist = TopicSuffixRegex().Replace(artist, "").Trim();
        return artist;
    }


    // SPLITTING


    /// <summary>Tries to split "Artist - Title" or "Artist ~ Title" into parts.</summary>
    public static (string Artist, string Title)? TrySplit(string rawTitle)
    {
        if (string.IsNullOrWhiteSpace(rawTitle)) return null;

        var cleaned = CleanTitle(rawTitle);
        cleaned = FeatureRegex().Replace(cleaned, string.Empty).Trim();

        // Try exotic separators first (PASTEL GHOST ~ POSSESSION)
        var exoticMatch = ExoticSeparatorRegex().Match(cleaned);
        if (exoticMatch.Success)
        {
            var artist = exoticMatch.Groups[1].Value.Trim();
            var title = exoticMatch.Groups[2].Value.Trim();
            if (artist.Length > 1 && title.Length > 1)
                return (artist, title);
        }

        // Fallback to classic dash separators
        foreach (var sep in ClassicSeparators)
        {
            var idx = cleaned.IndexOf(sep, StringComparison.Ordinal);
            if (idx <= 0) continue;

            var artist = cleaned[..idx].Trim();
            var title = cleaned[(idx + sep.Length)..].Trim();

            if (artist.Length > 0 && title.Length > 0)
                return (artist, title);
        }

        return null;
    }

    /// <summary>Splits "Artist1, Artist2 & Artist3" into individual credits.</summary>
    public static List<string> SplitArtistCredits(string artist)
    {
        if (string.IsNullOrWhiteSpace(artist)) return new List<string>();
        return ArtistSeparatorRegex()
            .Split(artist)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();
    }


    // NORMALIZATION (for grouping/searching)


    /// <summary>Normalizes artist name for grouping: lowercase, collapse separators, strip format chars.</summary>
    public static string NormalizeArtistKey(string artist)
    {
        if (string.IsNullOrWhiteSpace(artist)) return string.Empty;
        
        var stripped = new string(artist.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.Format).ToArray());
        var collapsed = WhitespaceRegex().Replace(stripped.Normalize(NormalizationForm.FormKC).Trim(), " ");
        return ArtistSeparatorRegex().Replace(collapsed, " & ").ToLowerInvariant();
    }

    /// <summary>Strips diacritics and non-alphanumeric chars for fuzzy comparison.</summary>
    public static string NormalizeForCompare(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var decomposed = s.Normalize(NormalizationForm.FormD);
        var stripped = new string(decomposed.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
        return new string(stripped.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
    }


    // IDENTITY (for duplicate detection)


    /// <summary>Generates a canonical key for "is this the same track?" Used by IsDuplicate, RemoveDuplicates, M3U import.</summary>
    public static string IdentityKey(string? title, string? artist, string? url = null, string? filePath = null)
    {
        // URL takes priority (exact match)
        if (!string.IsNullOrWhiteSpace(url))
            return $"url:{url.ToLowerInvariant()}";
        
        // FilePath takes second priority (exact match)
        if (!string.IsNullOrWhiteSpace(filePath))
            return $"path:{filePath.ToLowerInvariant()}";
        
        // Fall back to normalized title+artist
        var normTitle = NormalizeForCompare(title ?? "");
        var normArtist = NormalizeForCompare(artist ?? "");
        return $"ta:{normArtist}|{normTitle}";
    }

    /// <summary>Returns true if two tracks represent the same song. Used by download duplicate guard, RemoveDuplicates.</summary>
    public static bool AreSameTrack(Track a, Track b)
    {
        if (a == null || b == null) return false;
        if (a.Id == b.Id) return true;
        
        // URL match
        if (!string.IsNullOrWhiteSpace(a.Url) && string.Equals(a.Url, b.Url, StringComparison.OrdinalIgnoreCase))
            return true;
        
        // FilePath match
        if (!string.IsNullOrWhiteSpace(a.FilePath) && string.Equals(a.FilePath, b.FilePath, StringComparison.OrdinalIgnoreCase))
            return true;
        
        // Title+Artist match (normalized)
        if (!string.IsNullOrWhiteSpace(a.Title) && !string.IsNullOrWhiteSpace(a.Artist) &&
            !string.IsNullOrWhiteSpace(b.Title) && !string.IsNullOrWhiteSpace(b.Artist))
        {
            var aKey = NormalizeForCompare(a.Title) + "|" + NormalizeForCompare(a.Artist);
            var bKey = NormalizeForCompare(b.Title) + "|" + NormalizeForCompare(b.Artist);
            return aKey == bKey;
        }
        
        return false;
    }


    // FUZZY MATCHING (for VerifyLinks)


    /// <summary>Loose matching for VerifyLinks: handles "Artist - Title" splits, decorations, partial matches.</summary>
    public static bool LooselyMatch(string storedTitle, string embeddedTitle, string storedArtist = "", string embeddedArtist = "")
    {
        var cST = CleanTitle(storedTitle);
        var cET = CleanTitle(embeddedTitle);
        var cEA = CleanArtist(embeddedArtist);

        // Handle "Artist - Title" in embedded
        if (cET.Contains(" - "))
        {
            var s = cET.Split(new[] { " - " }, 2, StringSplitOptions.None);
            if (s.Length == 2)
            {
                var lN = NormalizeArtistKey(s[0]);
                var sAN = NormalizeArtistKey(storedArtist);
                var eAN = NormalizeArtistKey(cEA);
                
                if (lN == sAN || lN == eAN || string.IsNullOrWhiteSpace(cEA) || cEA.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                {
                    cEA = string.IsNullOrWhiteSpace(cEA) ? s[0].Trim() : cEA;
                    cET = s[1].Trim();
                }
            }
        }

        // Handle "Artist - Title" in stored
        if (cST.Contains(" - "))
        {
            var s = cST.Split(new[] { " - " }, 2, StringSplitOptions.None);
            if (s.Length == 2 && NormalizeArtistKey(s[0]) == NormalizeArtistKey(storedArtist))
                cST = s[1].Trim();
        }

        var a = NormalizeForCompare(cST);
        var b = NormalizeForCompare(cET);
        
        if (a.Length == 0 || b.Length == 0) return true;
        if (a == b) return true;

        var (shorter, longer) = a.Length <= b.Length ? (a, b) : (b, a);
        if (!longer.Contains(shorter, StringComparison.Ordinal)) return false;

        var (sRaw, lRaw) = a.Length <= b.Length ? (cST, cET) : (cET, cST);
        var known = Tokens(sRaw).Concat(Tokens(storedArtist)).Concat(Tokens(cEA)).ToHashSet(StringComparer.Ordinal);
        
        foreach (var t in ExtractContextTokens(lRaw)) known.Add(t);

        return !Tokens(lRaw).Any(t => !known.Contains(t) && !DecorationTokens.Contains(t));
    }


    // PLACEHOLDER DETECTION


    public static bool IsPlaceholderTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return true;
        if (PlaceholderTitles.Contains(title)) return true;
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


    // HELPERS (private)


    private static string FlattenUnicodeFonts(string input) =>
        string.IsNullOrEmpty(input) ? input : input.Normalize(NormalizationForm.FormKC);

    private static string ApplyScrubbingPasses(string input)
    {
        input = BracketGarbageRegex().Replace(input, "");
        input = LooseGarbageRegex().Replace(input, "");
        input = WhitespaceRegex().Replace(input, " ");
        input = TrailingPunctuationRegex().Replace(input, "");
        input = TrailingFeatureRegex().Replace(input, "");
        return input.Trim();
    }

    private static IEnumerable<string> ExtractContextTokens(string raw)
    {
        var tokens = new List<string>();
        foreach (Match m in FeatureArtistRegex().Matches(raw))
            tokens.AddRange(Tokens(m.Groups[1].Value));
        foreach (Match m in BracketContentRegex().Matches(raw))
            tokens.AddRange(Tokens(m.Groups[1].Value));
        return tokens;
    }

    private static HashSet<string> Tokens(string s) =>
        Regex.Matches(s.ToLowerInvariant(), @"[a-z0-9]+")
            .Select(m => m.Value)
            .Where(w => w.Length > 1)
            .ToHashSet();

    public static bool HasExoticSeparator(string s) =>
        s.Contains('~') || s.Contains('∞') || s.Contains('·') || s.Contains('•') || s.Contains('⧸') || s.Contains("///");
}