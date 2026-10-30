using NullWave.Helpers;
using NullWave.Models;
using Xunit;

namespace NullWave.Tests;

public class SourceDetectorTests
{
    // ---------- Works today ----------

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=abc123", TrackSource.YouTube)]
    [InlineData("https://youtu.be/abc123", TrackSource.YouTube)]
    [InlineData("https://music.youtube.com/watch?v=abc123", TrackSource.YouTube)]
    [InlineData("youtu.be/abc123", TrackSource.YouTube)]
    [InlineData("https://open.spotify.com/track/0DDrhNi7QBhJkSK3lL9ghD", TrackSource.Spotify)]
    [InlineData("https://soundcloud.com/c418/axolotl", TrackSource.SoundCloud)]
    [InlineData("https://on.soundcloud.com/AbCdEf", TrackSource.SoundCloud)]
    [InlineData("https://www.last.fm/music/Tame+Impala/_/Let+It+Happen", TrackSource.LastFm)]
    [InlineData("https://tameimpala.bandcamp.com/track/let-it-happen", TrackSource.Bandcamp)]
    [InlineData("https://audiomack.com/artist/song/example", TrackSource.Audiomack)]
    [InlineData("https://archive.org/details/GratefulDead1977-05-08", TrackSource.ArchiveOrg)]
    [InlineData("https://www.mixcloud.com/example/dj-mix/", TrackSource.Mixcloud)]
    public void Detect_recognises_known_sources(string url, TrackSource expected)
        => Assert.Equal(expected, SourceDetector.Detect(url));

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=abc123")]
    [InlineData("https://www.youtube.com/watch?v=abc123&list=PLxyz")]
    [InlineData("https://youtu.be/abc123")]
    [InlineData("https://www.youtube.com/playlist?list=PL123")]
    [InlineData("https://soundcloud.com/c418/axolotl")]
    [InlineData("https://soundcloud.com/c418/axolotl/")]
    [InlineData("https://open.spotify.com/track/0DDrhNi7QBhJkSK3lL9ghD")]
    [InlineData("https://open.spotify.com/album/4UMfKzi3CJKGDVZU7k9Wvr?si=f3f7624f55d348c0")]
    [InlineData("https://open.spotify.com/playlist/37i9dQZF1DXcBWIGoYBM5M")]
    [InlineData("https://www.last.fm/music/Tame+Impala")]
    [InlineData(@"C:\Music\song.mp3")]
    public void IsPlayableUrl_accepts_real_media_urls(string url)
        => Assert.True(SourceDetector.IsPlayableUrl(url));

    [Theory]
    [InlineData("https://bandcamp.com/some/track")]
    [InlineData("https://artist.bandcamp.com/")]
    [InlineData("https://artist.bandcamp.com/music")]
    [InlineData("https://audiomack.com/artist")]
    [InlineData("https://www.mixcloud.com/user/")]
    [InlineData("https://www.mixcloud.com/user/uploads/")]
    public void IsPlayableUrl_rejects_non_track_pages_and_lookalikes(string url)
        => Assert.False(SourceDetector.IsPlayableUrl(url));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://www.youtube.com/")]
    [InlineData("https://www.youtube.com/@c418")]
    [InlineData("https://soundcloud.com/")]
    [InlineData("https://soundcloud.com/c418")]
    [InlineData("https://open.spotify.com/")]
    [InlineData("https://open.spotify.com/artist/4uFZsG1vXrPcvnZ4iSQyrx")]
    [InlineData("https://www.last.fm/")]
    public void IsPlayableUrl_rejects_bare_roots_and_non_track_pages(string url)
        => Assert.False(SourceDetector.IsPlayableUrl(url));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"C:\Music\song.mp3")]
    [InlineData("/home/alex/Music/song.flac")]
    public void Detect_gives_Unknown_for_empty_local_or_other_sites(string? url)
        => Assert.Equal(TrackSource.Unknown, SourceDetector.Detect(url!));

    [Theory]
    [InlineData("https://notyoutube.com/watch?v=abc123", TrackSource.Unknown)]
    [InlineData("https://example.com/?u=https://youtu.be/abc123", TrackSource.Unknown)]
    [InlineData(@"C:\Music\spotify.com mix.mp3", TrackSource.Unknown)]
    [InlineData("HTTPS://YOUTU.BE/abc123", TrackSource.YouTube)]
    public void Detect_matches_the_host_not_any_substring(string url, TrackSource expected)
        => Assert.Equal(expected, SourceDetector.Detect(url));

    [Theory]
    [InlineData("https://www.youtube.com/shorts/abc123")]
    [InlineData("https://on.soundcloud.com/AbCdEf")]
    [InlineData("soundcloud.com/c418/axolotl")]
    public void IsPlayableUrl_accepts_shorts_share_links_and_urls_without_scheme(string url)
        => Assert.True(SourceDetector.IsPlayableUrl(url));

    // --- PATCH A: new sources ---

    [Theory]
    [InlineData("https://artist.bandcamp.com/track/slug")]
    [InlineData("https://audiomack.com/artist/song/slug")]
    [InlineData("https://archive.org/details/some-item")]
    [InlineData("https://www.mixcloud.com/user/some-mix/")]
    public void IsPlayableUrl_accepts_new_sources(string url) 
        => Assert.True(SourceDetector.IsPlayableUrl(url));

    [Theory]
    [InlineData("https://artist.bandcamp.com/")]
    [InlineData("https://artist.bandcamp.com/music")]
    [InlineData("https://artist.bandcamp.com/album/slug")]
    [InlineData("https://bandcamp.com/some/track")]
    [InlineData("https://audiomack.com/artist")]
    [InlineData("https://audiomack.com/artist/album/slug")]
    [InlineData("https://archive.org/")]
    [InlineData("https://www.mixcloud.com/user/")]
    [InlineData("https://www.mixcloud.com/user/uploads/")]
    public void IsPlayableUrl_rejects_non_track_pages_of_new_sources(string url) 
        => Assert.False(SourceDetector.IsPlayableUrl(url));

    [Theory]
    [InlineData("https://notbandcamp.com/track/x")]
    [InlineData("https://bandcamp.com.evil.test/track/x")]
    public void Detect_rejects_lookalike_hosts(string url) 
        => Assert.Equal(TrackSource.Unknown, SourceDetector.Detect(url));

    [Theory]
    [InlineData(TrackSource.YouTube, true)] 
    [InlineData(TrackSource.SoundCloud, true)] 
    [InlineData(TrackSource.Bandcamp, true)]
    [InlineData(TrackSource.Audiomack, true)] 
    [InlineData(TrackSource.ArchiveOrg, true)] 
    [InlineData(TrackSource.Mixcloud, true)]
    [InlineData(TrackSource.Spotify, false)] 
    [InlineData(TrackSource.LastFm, false)]
    [InlineData(TrackSource.Local, false)] 
    [InlineData(TrackSource.Unknown, false)]
    public void IsYtDlpSource_matches_the_table(TrackSource s, bool expected) 
        => Assert.Equal(expected, SourceDetector.IsYtDlpSource(s));

    // --- REGRESSION: direct stream URLs must stay playable ---

    [Theory]
    [InlineData("https://ice.somafm.com/groovesalad-128-mp3")]
    [InlineData("http://stream.example.com:8000/live")]
    [InlineData("https://example.com/radio/live.m3u8")]
    public void IsPlayableUrl_still_accepts_direct_stream_urls(string url)
        => Assert.True(SourceDetector.IsPlayableUrl(url));

    // --- REGRESSION: Mixcloud reserved user/page slugs ---

    [Theory]
    [InlineData("https://www.mixcloud.com/discover/jazz/")]
    [InlineData("https://www.mixcloud.com/user/stream/")]
    public void IsPlayableUrl_rejects_mixcloud_reserved_pages(string url)
        => Assert.False(SourceDetector.IsPlayableUrl(url));

    [Fact] 
    public void NormalizeUrl_extracts_the_target_of_a_markdown_link()
    { 
        const string u = "https://a.bandcamp.com/track/x"; 
        Assert.Equal(u, SourceDetector.NormalizeUrl($"[{u}]({u})")); 
    }

    [Theory]
    [InlineData("<https://a.bandcamp.com/track/x>", "https://a.bandcamp.com/track/x")]
    [InlineData("a.bandcamp.com/track/x", "https://a.bandcamp.com/track/x")]
    [InlineData("youtu.be/abc123", "https://youtu.be/abc123")]
    [InlineData("https://youtu.be/abc123", "https://youtu.be/abc123")]
    [InlineData(@"C:\Music\song.mp3", @"C:\Music\song.mp3")]
    public void NormalizeUrl_cases(string input, string expected) 
        => Assert.Equal(expected, SourceDetector.NormalizeUrl(input));

    [Theory]
    [InlineData("hello world")]
    [InlineData("<artist>/song/<name>")]
    public void IsPlayableUrl_rejects_text_that_is_not_a_url_or_path(string t) 
        => Assert.False(SourceDetector.IsPlayableUrl(t));
}