using NullWave.Helpers;
using NullWave.Models;
using Xunit;

namespace NullWave.Tests;

public class TrackNormalizerTests
{

    // CLEANING


    [Theory]
    [InlineData("PASTEL GHOST - DARK BEACH (Official Video)", "PASTEL GHOST - DARK BEACH")]
    [InlineData("Tame Impala - The Less I Know The Better [Lyric Video]", "Tame Impala - The Less I Know The Better")]
    [InlineData("Eminem - Rap God (Explicit Version)", "Eminem - Rap God")]
    [InlineData("𝕳𝖊𝖑𝖑𝖔 𝖂𝖔𝖗𝖑𝖉", "Hello World")]
    public void CleanTitle_removes_YouTube_clutter(string input, string expected)
    {
        Assert.Equal(expected, TrackNormalizer.CleanTitle(input));
    }

    [Theory]
    [InlineData("EminemMusic", "Eminem")]
    [InlineData("EminemVEVO", "Eminem")]
    [InlineData("Eminem - Topic", "Eminem")]
    [InlineData("Eminem", "Eminem")]
    public void CleanArtist_removes_suffixes(string input, string expected)
    {
        Assert.Equal(expected, TrackNormalizer.CleanArtist(input));
    }


    // SPLITTING


    [Theory]
    [InlineData("PASTEL GHOST ~ DARK BEACH", "PASTEL GHOST", "DARK BEACH")]
    [InlineData("Tame Impala - The Less I Know The Better", "Tame Impala", "The Less I Know The Better")]
    [InlineData("Dr. Dre – I Need A Doctor", "Dr. Dre", "I Need A Doctor")]
    [InlineData("Eminem — Lose Yourself", "Eminem", "Lose Yourself")]
    public void TrySplit_handles_various_separators(string input, string expectedArtist, string expectedTitle)
    {
        var result = TrackNormalizer.TrySplit(input);
        Assert.NotNull(result);
        Assert.Equal(expectedArtist, result.Value.Artist);
        Assert.Equal(expectedTitle, result.Value.Title);
    }

    [Fact]
    public void TrySplit_returns_null_for_no_separator()
    {
        Assert.Null(TrackNormalizer.TrySplit("Just a title without artist"));
    }

    [Theory]
    [InlineData("Eminem, Dr. Dre & Skylar Grey", 3)]
    [InlineData("Eminem feat. Rihanna", 2)]
    [InlineData("Eminem ft. Nate Dogg", 2)]
    [InlineData("Solo Artist", 1)]
    public void SplitArtistCredits_counts_correctly(string input, int expectedCount)
    {
        Assert.Equal(expectedCount, TrackNormalizer.SplitArtistCredits(input).Count);
    }


    // NORMALIZATION


    [Theory]
    [InlineData("Eminem", "eminem")]
    [InlineData("Dr. Dre", "dr. dre")]
    [InlineData("Eminem, Dr. Dre", "eminem & dr. dre")]
    [InlineData("Eminem & Dr. Dre", "eminem & dr. dre")]
    [InlineData("  Eminem  ", "eminem")]
    public void NormalizeArtistKey_produces_consistent_keys(string input, string expected)
    {
        Assert.Equal(expected, TrackNormalizer.NormalizeArtistKey(input));
    }

    [Theory]
    [InlineData("Café", "cafe")]
    [InlineData("Naïve", "naive")]
    [InlineData("HELLO WORLD", "helloworld")]
    [InlineData("Hello, World!", "helloworld")]
    public void NormalizeForCompare_strips_diacritics_and_punctuation(string input, string expected)
    {
        Assert.Equal(expected, TrackNormalizer.NormalizeForCompare(input));
    }


    // IDENTITY


    [Fact]
    public void IdentityKey_prefers_url_over_path_over_title()
    {
        var key1 = TrackNormalizer.IdentityKey("Title", "Artist", "https://youtube.com/watch?v=abc", "/path/to/file.mp3");
        Assert.StartsWith("url:", key1);

        var key2 = TrackNormalizer.IdentityKey("Title", "Artist", null, "/path/to/file.mp3");
        Assert.StartsWith("path:", key2);

        var key3 = TrackNormalizer.IdentityKey("Title", "Artist", null, null);
        Assert.StartsWith("ta:", key3);
    }

    [Fact]
    public void AreSameTrack_matches_by_url()
    {
        var a = new Track { Id = Guid.NewGuid(), Url = "https://youtube.com/watch?v=abc" };
        var b = new Track { Id = Guid.NewGuid(), Url = "https://youtube.com/watch?v=abc" };
        Assert.True(TrackNormalizer.AreSameTrack(a, b));
    }

    [Fact]
    public void AreSameTrack_matches_by_title_artist()
    {
        var a = new Track { Id = Guid.NewGuid(), Title = "Lose Yourself", Artist = "Eminem" };
        var b = new Track { Id = Guid.NewGuid(), Title = "Lose Yourself", Artist = "Eminem" };
        Assert.True(TrackNormalizer.AreSameTrack(a, b));
    }

    [Fact]
    public void AreSameTrack_rejects_different_tracks()
    {
        var a = new Track { Id = Guid.NewGuid(), Title = "Lose Yourself", Artist = "Eminem" };
        var b = new Track { Id = Guid.NewGuid(), Title = "Stan", Artist = "Eminem" };
        Assert.False(TrackNormalizer.AreSameTrack(a, b));
    }


    // FUZZY MATCHING


    [Theory]
    [InlineData("Lose Yourself", "Eminem", "Lose Yourself", "Eminem", true)]
    [InlineData("Lose Yourself", "Eminem", "Lose Yourself (Official Video)", "Eminem", true)]
    [InlineData("Stan", "Eminem", "Lose Yourself", "Eminem", false)]
    public void LooselyMatch_handles_decorations(string t1, string a1, string t2, string a2, bool expected)
    {
        // LooselyMatch signature: (storedTitle, embeddedTitle, storedArtist, embeddedArtist)
        Assert.Equal(expected, TrackNormalizer.LooselyMatch(t1, t2, a1, a2));
    }


    // PLACEHOLDER DETECTION


    [Theory]
    [InlineData("Unknown Title", true)]
    [InlineData("YouTube track", true)]
    [InlineData("https://youtube.com/watch?v=abc", true)]
    [InlineData("Real Title", false)]
    [InlineData("", true)]
    [InlineData(null, true)]
    public void IsPlaceholderTitle_detects_correctly(string? input, bool expected)
    {
        Assert.Equal(expected, TrackNormalizer.IsPlaceholderTitle(input));
    }

    [Theory]
    [InlineData("Unknown", true)]
    [InlineData("Unknown Artist", true)]
    [InlineData("Eminem", false)]
    [InlineData("", true)]
    [InlineData(null, true)]
    public void IsPlaceholderArtist_detects_correctly(string? input, bool expected)
    {
        Assert.Equal(expected, TrackNormalizer.IsPlaceholderArtist(input));
    }
}