using NullWave.Models;
using NullWave.ViewModels;
using Xunit;

namespace NullWave.Tests;

public class DetectMediaTypeTests
{
    [Theory]
    [InlineData("https://artist.bandcamp.com/track/streams-of-light", MediaType.Music)]
    [InlineData("https://www.mixcloud.com/user/radio-stream-show/", MediaType.Music)]
    [InlineData("https://archive.org/details/audiobook-moby-dick", MediaType.Audiobook)]
    [InlineData("https://example.com/live.m3u8", MediaType.Radio)]
    public void Media_type_follows_the_source_not_a_url_substring(string url, MediaType expected)
        => Assert.Equal(expected, TrackInputViewModel.DetectMediaType(url));
}