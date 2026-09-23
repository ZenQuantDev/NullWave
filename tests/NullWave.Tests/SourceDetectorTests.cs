using NullWave.Helpers;
using NullWave.Models;
using Xunit;

namespace NullWave.Tests;

public class SourceDetectorTests
{
    private const string GapReason =
        "Known gap in the original SourceDetector - remove Skip after applying the patched version";

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
    public void Detect_recognises_known_sources(string url, TrackSource expected)
        => Assert.Equal(expected, SourceDetector.Detect(url));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"C:\Music\song.mp3")]
    [InlineData("/home/alex/Music/song.flac")]
    [InlineData("https://bandcamp.com/some/track")]
    public void Detect_gives_Unknown_for_empty_local_or_other_sites(string? url)
        => Assert.Equal(TrackSource.Unknown, SourceDetector.Detect(url!));

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
    [InlineData("https://bandcamp.com/some/track")]
    public void IsPlayableUrl_accepts_real_media_urls(string url)
        => Assert.True(SourceDetector.IsPlayableUrl(url));

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

    // ---------- Known gaps (skipped until SourceDetector is patched) ----------

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
}