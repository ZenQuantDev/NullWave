using NullWave.Services.Metadata;
using Xunit;

namespace NullWave.Tests;

public class TrackTitleParserTests
{
    private const string GapReason = "Known gap in TrackTitleParser - remove Skip after applying the fix";

    // ---------- Works today ----------

    [Theory]
    [InlineData("Tame Impala - Let It Happen (Official Video)", "Tame Impala", "Let It Happen")]
    [InlineData("Artist - Song ft. Someone Else", "Artist", "Song")]
    [InlineData("Artist - Song [HD]", "Artist", "Song")]
    [InlineData("Artist - Song (Director's Cut)", "Artist", "Song")]
    [InlineData("Artist – Song", "Artist", "Song")]
    [InlineData("PASTEL GHOST ~ POSSESSION", "PASTEL GHOST", "POSSESSION")]
    [InlineData("Artist · Song", "Artist", "Song")]
    [InlineData("Artist // Song", "Artist", "Song")]
    [InlineData("Artist ⧸ Song", "Artist", "Song")]
    [InlineData("Artist ~ Song ~ Remix", "Artist", "Song ~ Remix")]
    public void TryParseArtistTitle_splits_artist_and_title(string raw, string artist, string title)
    {
        var parsed = TrackTitleParser.TryParseArtistTitle(raw);

        Assert.NotNull(parsed);
        Assert.Equal(artist, parsed!.Value.Artist);
        Assert.Equal(title, parsed.Value.Title);
    }

    [Theory]
    [InlineData("Just A Title")]
    [InlineData("A-ha")]
    [InlineData("Artist - ")]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParseArtistTitle_returns_null_without_a_separator(string raw)
        => Assert.Null(TrackTitleParser.TryParseArtistTitle(raw));

    [Theory]
    [InlineData("PASTEL GHOST ~ POSSESSION", true)]
    [InlineData("Artist /// Song", true)]
    [InlineData("Artist ∞ Song", true)]
    [InlineData("Artist - Song", false)]
    [InlineData("", false)]
    public void HasExoticSeparator_detects_non_dash_separators(string text, bool expected)
        => Assert.Equal(expected, TrackTitleParser.HasExoticSeparator(text));

    [Theory]
    [InlineData("Tame Impala - Let It Happen (Official Video)", "Tame Impala", "Tame Impala", "Let It Happen")]
    [InlineData("Tame Impala - Let It Happen", "Unknown", "Tame Impala", "Let It Happen")]
    [InlineData("Tame Impala - Let It Happen", "Unknown Artist", "Tame Impala", "Let It Happen")]
    [InlineData("Tame Impala - Let It Happen", "", "Tame Impala", "Let It Happen")]
    [InlineData("Song", "AdeleVEVO", "Adele", "Song")]
    [InlineData("Song", "Tame Impala - Topic", "Tame Impala", "Song")]
    [InlineData("Song [Official Music Video]", "Artist", "Artist", "Song")]
    [InlineData("Song (Video)", "Artist", "Artist", "Song")]
    public void CleanYouTubeMetadata_cleans_title_and_artist(
        string rawTitle, string rawArtist, string artist, string title)
        => Assert.Equal((artist, title), TrackTitleParser.CleanYouTubeMetadata(rawTitle, rawArtist));

    [Fact]
    public void CleanYouTubeMetadata_handles_null_input()
        => Assert.Equal((string.Empty, string.Empty), TrackTitleParser.CleanYouTubeMetadata(null!, null!));

    // DECISION NEEDED: an uploader channel name wins over the artist written in the title.
    // Fine for "Artist - Topic" or VEVO channels, probably wrong for random uploaders.
    [Fact]
    public void Channel_name_wins_over_the_artist_in_the_title_today()
        => Assert.Equal(("Random Channel", "Let It Happen"),
                        TrackTitleParser.CleanYouTubeMetadata("Tame Impala - Let It Happen", "Random Channel"));

    // ---------- Known gaps (skipped until TrackTitleParser is fixed) ----------

    [Fact]
    public void TryParseArtistTitle_splits_on_em_dash()
    {
        var parsed = TrackTitleParser.TryParseArtistTitle("Artist — Song");

        Assert.NotNull(parsed);
        Assert.Equal("Artist", parsed!.Value.Artist);
        Assert.Equal("Song", parsed.Value.Title);
    }

    [Fact]
    public void TryParseArtistTitle_removes_official_lyric_video_tag()
    {
        var parsed = TrackTitleParser.TryParseArtistTitle("Artist - Song (Official Lyric Video)");

        Assert.NotNull(parsed);
        Assert.Equal("Song", parsed!.Value.Title);
    }

    [Theory]
    [InlineData("Artist - Song (Official Audio)", "Artist", "Artist", "Song")]              // currently "Song ()"
    [InlineData("Let It Happen (Official Music Video)", "Tame Impala", "Tame Impala", "Let It Happen")]
    public void CleanYouTubeMetadata_removes_leftover_tags(
        string rawTitle, string rawArtist, string artist, string title)
        => Assert.Equal((artist, title), TrackTitleParser.CleanYouTubeMetadata(rawTitle, rawArtist));
}