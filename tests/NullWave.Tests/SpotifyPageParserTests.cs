using NullWave.Services;
using Xunit;

namespace NullWave.Tests;

public class SpotifyPageParserTests
{
    // Layout copied from a real track page (Axolotl by C418). Replace with a saved page
    // (Invoke-WebRequest <url> | Select-Object -ExpandProperty Content) to confirm the raw HTML.
    private const string TrackHtml = """
        <html><head>
        <meta property="og:type" content="music.song"/>
        <meta property="og:title" content="Axolotl"/>
        <meta property="og:description" content="C418 · Axolotl · Song · 2018"/>
        <meta name="music:musician_description" content="C418"/>
        <meta name="music:duration" content="303"/>
        </head></html>
        """;

    private const string AlbumHtml = """
        <html><head>
        <meta property="og:type" content="music.album"/>
        <meta property="og:title" content="Axolotl - Single by C418 | Spotify"/>
        <meta property="og:description" content="C418 · single · 2018 · 1 songs"/>
        <meta name="music:song" content="https://open.spotify.com/track/0DDrhNi7QBhJkSK3lL9ghD"/>
        </head></html>
        """;

    [Fact]
    public void Track_page_gives_title_artist_and_duration()
    {
        var page = SpotifyPageParser.Parse(TrackHtml);

        Assert.Equal(SpotifyPageKind.Track, page.Kind);
        Assert.Equal("Axolotl", page.Title);
        Assert.Equal("C418", page.Artist);
        Assert.Equal(303, page.DurationSeconds);
    }

    [Fact]
    public void Album_page_strips_suffix_and_lists_tracks()
    {
        var page = SpotifyPageParser.Parse(AlbumHtml);

        Assert.Equal(SpotifyPageKind.Album, page.Kind);
        Assert.Equal("Axolotl", page.Title);
        Assert.Equal("C418", page.Artist);
        Assert.Single(page.TrackUrls);
        Assert.EndsWith("0DDrhNi7QBhJkSK3lL9ghD", page.TrackUrls[0]);
    }

    [Theory]
    [InlineData("Currents - Album by Tame Impala | Spotify", "Currents")]
    [InlineData("Cookie Clicker - EP by C418 | Spotify", "Cookie Clicker")]
    [InlineData("Minecraft - Volume Alpha - Album by C418 | Spotify", "Minecraft - Volume Alpha")]
    [InlineData("Already Clean", "Already Clean")]
    public void Album_title_suffix_is_removed(string ogTitle, string expected)
    {
        var html = $"<meta property=\"og:title\" content=\"{ogTitle}\"/>";

        Assert.Equal(expected, SpotifyPageParser.Parse(html).Title);
    }

    [Fact]
    public void Attribute_order_and_html_entities_do_not_matter()
    {
        var html = "<meta content=\"Rock &amp; Roll\" property=\"og:title\"/>";

        Assert.Equal("Rock & Roll", SpotifyPageParser.Parse(html).Title);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html></html>")]
    public void Missing_tags_give_unknown_and_empty(string html)
    {
        var page = SpotifyPageParser.Parse(html);

        Assert.Equal(SpotifyPageKind.Unknown, page.Kind);
        Assert.Equal(string.Empty, page.Title);
        Assert.Empty(page.TrackUrls);
    }

    [Fact]
    public void CleanUrl_removes_tracking_query()
    {
        var clean = SpotifyPageParser.CleanUrl("https://open.spotify.com/album/4UMfKzi3CJKGDVZU7k9Wvr?si=f3f7624f55d348c0");

        Assert.Equal("https://open.spotify.com/album/4UMfKzi3CJKGDVZU7k9Wvr", clean);
    }
}