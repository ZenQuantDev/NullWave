using System.Text;
using NullWave.Services.Metadata;
using Xunit;

namespace NullWave.Tests;

public class TitleSanitizerTests
{
    private const string GapReason = "Known gap in TitleSanitizer - remove Skip after applying the fix";

    // Builds "Mathematical Bold" text like 𝐓𝐀𝐌𝐄 (A-Z and a-z), the kind of fancy font some uploaders use.
    private static string MathBold(string text)
    {
        var sb = new StringBuilder();
        foreach (var c in text)
        {
            if (c >= 'A' && c <= 'Z') sb.Append(char.ConvertFromUtf32(0x1D400 + (c - 'A')));
            else if (c >= 'a' && c <= 'z') sb.Append(char.ConvertFromUtf32(0x1D41A + (c - 'a')));
            else sb.Append(c);
        }
        return sb.ToString();
    }

    // ---------- Works today ----------

    [Theory]
    [InlineData("Tame Impala - Let It Happen (Official Video)", "Tame Impala", "Let It Happen")]
    [InlineData("Tame Impala - The Less I Know The Better [Official Music Video]", "Tame Impala", "The Less I Know The Better")]
    [InlineData("Artist - Song (Lyrics)", "Artist", "Song")]
    [InlineData("Artist - Song | Official Video", "Artist", "Song")]
    [InlineData("Artist - Song (Official Video) [HD]", "Artist", "Song")]
    [InlineData("Artist - Song (Official Lyric Video)", "Artist", "Song")]
    [InlineData("Artist - Song [Explicit]", "Artist", "Song")]
    [InlineData("Artist - Song (Clean)", "Artist", "Song")]
    [InlineData("Artist - Song ft.", "Artist", "Song")]
    [InlineData("PASTEL GHOST ~ POSSESSION", "PASTEL GHOST", "POSSESSION")]
    [InlineData("A-ha - Take On Me", "A-ha", "Take On Me")]
    [InlineData("Song Title", "", "Song Title")]
    public void Sanitize_splits_artist_and_removes_platform_clutter(string raw, string artist, string title)
        => Assert.Equal((artist, title), TitleSanitizer.Sanitize(raw));

    [Theory]
    [InlineData("Artist - Song (Live at Wembley)", "Song (Live at Wembley)")]
    [InlineData("Artist - Song (feat. Someone)", "Song (feat. Someone)")]
    [InlineData("Artist - Song - Live", "Song - Live")]
    [InlineData("Daft Punk - Get Lucky (Official Audio) ft. Pharrell", "Get Lucky ft. Pharrell")]
    public void Sanitize_keeps_meaningful_title_parts(string raw, string expectedTitle)
        => Assert.Equal(expectedTitle, TitleSanitizer.Sanitize(raw).Title);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Sanitize_returns_empty_for_blank_input(string? raw)
        => Assert.Equal((string.Empty, string.Empty), TitleSanitizer.Sanitize(raw!));

    [Theory]
    [InlineData("Song (Official Audio)", "Song")]
    [InlineData("Song [Explicit]", "Song")]
    [InlineData("Song (Live)", "Song (Live)")]
    [InlineData("Song - ", "Song")]
    [InlineData("   ", "")]
    public void SanitizeSingle_cleans_one_field(string text, string expected)
        => Assert.Equal(expected, TitleSanitizer.SanitizeSingle(text));

    [Fact]
    public void Bold_capital_letters_are_flattened()
    {
        var raw = MathBold("TAME IMPALA") + " - " + MathBold("SONG");

        Assert.Equal(("TAME IMPALA", "SONG"), TitleSanitizer.Sanitize(raw));
    }

    // DECISION NEEDED: a remix is a different recording, but its tag is stripped today.
    // Keep this test if that is what you want; change it if remixes should stay in the title.
    [Fact]
    public void Remix_tag_is_removed_today()
        => Assert.Equal("Song", TitleSanitizer.Sanitize("Artist - Song (Four Tet Remix)").Title);

    // ---------- Known gaps (skipped until TitleSanitizer is fixed) ----------

    [Theory]
    [InlineData("Artist – Song", "Artist", "Song")]   // en dash
    [InlineData("Artist — Song", "Artist", "Song")]   // em dash
    public void Sanitize_splits_on_en_and_em_dashes(string raw, string artist, string title)
        => Assert.Equal((artist, title), TitleSanitizer.Sanitize(raw));

    [Fact]
    public void Lowercase_and_other_fancy_fonts_are_flattened()
    {
        var raw = MathBold("Tame Impala") + " - " + MathBold("Let It Happen");

        Assert.Equal(("Tame Impala", "Let It Happen"), TitleSanitizer.Sanitize(raw));
    }
}