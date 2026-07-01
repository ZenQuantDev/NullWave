// TitleSanitizer.cs
using System;
using System.Text;
using System.Text.RegularExpressions;

namespace NullWave.Services.Metadata;

public static class TitleSanitizer
{
    // Captures ANY bracket set containing platform fluff words anywhere inside it
    private static readonly Regex BracketGarbageRegex = new Regex(
        @"[\(\[\{「【][^\)\]\}「】]*?\b(?:official|video|audio|music|lyric|lyrics|visualizer|clip|remastered|remaster|explicit|clean|version|hq|hd|4k|uncensored|edit|download|caption|captions|cc|unreleased|long|cut|mono|stereo|spatial|atmos|remix)s?\b[^\)\]\}「】]*?[\)\]\}」】]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex LooseGarbageRegex = new Regex(
        @"\b(official\s+video|official\s+music\s+video|official\s+audio|lyric\s+video|lyrics|official\s+visualizer|unreleased|remastered|explicit\s+version|clean\s+version)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TrailingFeatureRegex = new Regex(
        @"[\s,\-\|]+(ft\.?|feat\.?|featuring|with)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] Dividers = { " - ", " ~ ", " | ", " // ", " ∞ " };

    /// <summary>
    /// Cleans a single raw title string, splitting out embedded artist data if dividers are present,
    /// flattening unicode formatting fonts, and stripping media garbage flags.
    /// </summary>
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

    /// <summary>
    /// Cleans a single field (artist OR title) without attempting to split on dividers.
    /// Use this when you already have separated fields from an API response.
    /// </summary>
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
        input = Regex.Replace(input, @"[\s\-\|,\•\·]+$", "");
        input = TrailingFeatureRegex.Replace(input, "");
        return input.Trim();
    }

    /// <summary>
    /// Translates Unicode Mathematical Alphanumeric block characters back to standard ASCII text.
    /// </summary>
    private static string FlattenUnicodeFonts(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        var sb = new StringBuilder();
        for (int i = 0; i < input.Length; i++)
        {
            if (char.IsSurrogatePair(input, i))
            {
                int codePoint = char.ConvertToUtf32(input, i);
                i++;
                if (codePoint >= 0x1D400 && codePoint <= 0x1D7FF)
                {
                    if (codePoint >= 0x1D400 && codePoint <= 0x1D419) { sb.Append((char)('A' + (codePoint - 0x1D400))); continue; }
                    if (codePoint >= 0x1D434 && codePoint <= 0x1D44D) { sb.Append((char)('A' + (codePoint - 0x1D434))); continue; }
                    if (codePoint >= 0x1D468 && codePoint <= 0x1D481) { sb.Append((char)('A' + (codePoint - 0x1D468))); continue; }
                    if (codePoint >= 0x1D49C && codePoint <= 0x1D4B5) { sb.Append((char)('A' + (codePoint - 0x1D49C))); continue; }
                }
                sb.Append(char.ConvertFromUtf32(codePoint));
            }
            else
            {
                sb.Append(input[i]);
            }
        }
        return sb.ToString();
    }
}