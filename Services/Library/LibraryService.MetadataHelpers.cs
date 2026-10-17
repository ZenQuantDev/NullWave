using System;
using System.IO;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NullWave.Models;
using NullWave.Helpers;

namespace NullWave.Services;

public partial class LibraryService
{
    private static readonly Regex ArtistSeparatorRegex = new(@"\s*(?:,|&|\bfeat\.?\b|\bft\.?\b|\bfeaturing\b)\s*", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly HashSet<string> DecorationTokens = new(StringComparer.Ordinal)
    { "official", "music", "video", "audio", "lyric", "lyrics", "explicit", "clean", "version", "radio", "edit", "remix", "remastered", "live", "acoustic", "ft", "feat", "featuring", "hd", "hq", "mv", "prod", "produced", "by", "and", "with", "of", "in", "on", "part", "pt", "the", "that", "this" };
    
    private static readonly Regex[] YouTubeArtifactTitleRegexes = new[]
    {
        new Regex(@"\s*[\(\[]\s*(?:Official\s*(?:Music\s*)?Video|Official\s*Audio|Official\s*Lyric\s*Video|Lyric\s*Video|Lyrics|Audio|Video|Visualizer|Remix|Live|Performance|Clip)\s*[\)\]]", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"\s*[\(\[]\s*(?:Explicit|Clean)\s*[\)\]]", RegexOptions.IgnoreCase | RegexOptions.Compiled)
    };
    private static readonly Regex YouTubeTopicArtistRegex = new(@"\s*-\s*Topic\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex FeatureArtistRegex = new(@"\b(?:ft\.?|feat\.?|featuring|with|vs\.?)\s+(.+?)(?=\s*[\(\[\-–-]|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BracketContentRegex = new(@"[\(\[](.*?)[\)\]]", RegexOptions.Compiled);

    [Obsolete("Use TrackNormalizer.CleanTitle or TrackNormalizer.CleanArtist instead")]
    public static string CleanYouTubeArtifacts(string input, bool isArtist = false)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;
        var cleaned = input;
        if (isArtist) cleaned = YouTubeTopicArtistRegex.Replace(cleaned, "");
        else foreach (var r in YouTubeArtifactTitleRegexes) cleaned = r.Replace(cleaned, "");
        return cleaned.Trim();
    }

    [Obsolete("Use TrackNormalizer.LooselyMatch instead")]
    internal static bool TitlesLooselyMatch(string storedTitle, string embeddedTitle, string storedArtist = "", string embeddedArtist = "")
    {
        // Delegate to the new unified implementation
        return TrackNormalizer.LooselyMatch(storedTitle, embeddedTitle, storedArtist, embeddedArtist);
    }

    private static IEnumerable<string> ExtractContextTokens(string raw)
    {
        var tokens = new List<string>();
        foreach (Match m in FeatureArtistRegex.Matches(raw)) tokens.AddRange(Tokens(m.Groups[1].Value));
        foreach (Match m in BracketContentRegex.Matches(raw)) tokens.AddRange(Tokens(m.Groups[1].Value));
        return tokens;
    }

    [Obsolete("Use TrackNormalizer.NormalizeForCompare instead")]
    private static string NormalizeForCompare(string s)
    {
        var d = (s ?? string.Empty).Normalize(NormalizationForm.FormD);
        var stripped = new string(d.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
        return new string(stripped.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
    }

    private bool HasVerifiedFile(Track t)
    {
        if (string.IsNullOrEmpty(t.FilePath) || !File.Exists(t.FilePath)) return false;
        if (_metadata == null) return true;
        try { var e = _metadata.FetchFromLocalFile(t.FilePath); return string.IsNullOrWhiteSpace(e.Title) || TrackNormalizer.LooselyMatch(t.Title, e.Title, t.Artist, e.Artist); }
        catch { return true; }
    }

    [Obsolete("Use TrackNormalizer.NormalizeArtistKey instead")]
    internal static string NormalizeArtistKey(string artist)
    {
        var stripped = new string(artist.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.Format).ToArray());
        var collapsed = Regex.Replace(stripped.Normalize(NormalizationForm.FormKC).Trim(), @"\s+", " ");
        return ArtistSeparatorRegex.Replace(collapsed, " & ").ToLowerInvariant();
    }

    [Obsolete("Use TrackNormalizer.SplitArtistCredits instead")]
    public static List<string> SplitArtistCredits(string artist) => 
        string.IsNullOrWhiteSpace(artist) ? new() : ArtistSeparatorRegex.Split(artist).Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

    private static HashSet<string> Tokenize(string s) => Regex.Matches(s.ToLowerInvariant(), @"[a-z0-9]+").Select(m => m.Value).Where(w => w.Length > 2).ToHashSet();
    private static List<string> Tokens(string s) => Regex.Matches(s.ToLowerInvariant(), @"[a-z0-9]+").Select(m => m.Value).Where(w => w.Length > 1).ToList();
}