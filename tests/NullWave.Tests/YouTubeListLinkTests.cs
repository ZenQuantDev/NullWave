using NullWave.Helpers;
using Xunit;

namespace NullWave.Tests;

public class YouTubeListLinkTests
{
    [Theory]
    [InlineData("https://www.youtube.com/watch?v=abc123DEF45&list=RDabc123DEF45&start_radio=1", YouTubeListKind.Mix)]
    [InlineData("https://www.youtube.com/watch?v=abc123DEF45&list=RDMM", YouTubeListKind.Mix)]
    [InlineData("https://youtu.be/abc123DEF45?list=RDabc123DEF45", YouTubeListKind.Mix)]
    [InlineData("https://www.youtube.com/watch?v=abc123DEF45&list=PLabcdef&index=3", YouTubeListKind.Playlist)]
    [InlineData("https://www.youtube.com/watch?v=abc123DEF45&list=LL", YouTubeListKind.Private)]
    [InlineData("https://www.youtube.com/watch?v=abc123DEF45&list=WL", YouTubeListKind.Private)]
    [InlineData("https://www.youtube.com/watch?v=abc123DEF45", YouTubeListKind.None)]
    [InlineData("https://soundcloud.com/a/b?list=RDx", YouTubeListKind.None)]
    [InlineData(null, YouTubeListKind.None)]
    public void Classify_table(string? url, YouTubeListKind expected)
        => Assert.Equal(expected, YouTubeListLink.Classify(url));

    [Fact]
    public void Default_normalize_strips_list_but_keepListParam_preserves_it()
    {
        const string mix = "https://www.youtube.com/watch?v=abc123DEF45&list=RDabc123DEF45&start_radio=1";
        Assert.DoesNotContain("list=", SourceDetector.NormalizeUrl(mix));
        Assert.Contains("list=RDabc123DEF45", SourceDetector.NormalizeUrl(mix, keepListParam: true));
    }
}