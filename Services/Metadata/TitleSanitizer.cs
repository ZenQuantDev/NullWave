using System;
using System.Text;
using System.Text.RegularExpressions;

namespace NullWave.Services.Metadata;

public static class TitleSanitizer
{
    private static readonly Regex BracketGarbageRegex = new Regex(
        @"[\(\[\{«「『][^\)\]\}»」』]*?\b(?:official|video|audio|music|lyric|lyrics|visualizer|clip|remastered|remaster|explicit|clean|version|hq|hd|4k|uncensored|edit|download|caption|captions|cc|unreleased|long|cut|mono|stereo|spatial|atmos|remix)s?\b[^\)\]\}»」』]*?[\)\]\}»」』]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex LooseGarbageRegex = new Regex(
        @"\b(official\s+video|official\s+music\s+video|official\s+audio|lyric\s+video|lyrics|official\s+visualizer|unreleased|remastered|explicit\s+version|clean\s+version)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TrailingFeatureRegex = new Regex(
        @"[\s,\-\|]+(ft\.?|feat\.?|featuring|with)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // FIX: Added en dash (–) and em dash (—)
    private static readonly string[] Dividers = { " - ", " – ", " — ", " ~ ", " | ", " // ", " ∞ " };

    public static (string Artist, string Title) Sanitize(string rawTitle)
    {
        if (string.IsNullOrWhiteSpace(rawTitle))
            return (string.Empty, string.Empty);

        string flattenedInput = FlattenUnicodeFonts(rawTitle);
        string artist = string.Empty;
        string title = flattenedInput;

        foreach (var divider in Dividers)
        {
            if (flattenedInput.Contains(divider))
            {
                var parts = flattenedInput.Split(new[] { divider }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    artist = parts[0].Trim();
                    title = string.Join(divider, parts, 1, parts.Length - 1).Trim();
                    break;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(artist))
        {
            artist = Regex.Replace(artist, @"\b-\s*Topic\b", "", RegexOptions.IgnoreCase).Trim();
        }

        return (artist.Trim(), ApplyScrubbingPasses(title));
    }

    public static string SanitizeSingle(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        string cleaned = FlattenUnicodeFonts(text);
        return ApplyScrubbingPasses(cleaned);
    }

    private static string ApplyScrubbingPasses(string input)
    {
        input = BracketGarbageRegex.Replace(input, "");
        input = LooseGarbageRegex.Replace(input, "");
        input = Regex.Replace(input, @"\s+", " ");
        input = Regex.Replace(input, @"[\s\-\|,•·]+$", "");
        input = TrailingFeatureRegex.Replace(input, "");
        return input.Trim();
    }

    // FIX: Replaced manual surrogate pair mapping with NFKC normalization
    private static string FlattenUnicodeFonts(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        return input.Normalize(NormalizationForm.FormKC);
    }
}