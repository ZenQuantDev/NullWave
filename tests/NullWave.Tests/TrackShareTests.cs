using System;
using NullWave.Helpers;
using Xunit;

namespace NullWave.Tests;

public class TrackShareTests
{
    [Fact]
    public void TrackUri_RoundTrip()
    {
        var installId = "peer-12345-abcde";
        var trackId = Guid.NewGuid();
        
        var uri = ShareLink.Track(installId, trackId);
        
        Assert.True(ShareLink.TryParseTrack(uri, out var parsedInstall, out var parsedTrack));
        Assert.Equal(installId, parsedInstall);
        Assert.Equal(trackId, parsedTrack);
    }

    [Fact]
    public void TrackUri_Rejects_Garbage()
    {
        Assert.False(ShareLink.TryParseTrack("not a uri", out _, out _));
        Assert.False(ShareLink.TryParseTrack("nullwave://peer/playlist/123", out _, out _));
        Assert.False(ShareLink.TryParseTrack("http://youtube.com/watch?v=123", out _, out _));
        Assert.False(ShareLink.TryParseTrack(null, out _, out _));
    }
    
    [Fact]
    public void TrackUri_Rejects_WrongScheme()
    {
        Assert.False(ShareLink.TryParseTrack("spotify://peer/track/123", out _, out _));
        Assert.False(ShareLink.TryParseTrack("https://nullwave.com/track/123", out _, out _));
    }

    [Fact]
    public void TrackUri_Rejects_InvalidGuid()
    {
        // Valid scheme and host, but the track ID isn't a valid GUID
        Assert.False(ShareLink.TryParseTrack("nullwave://peer-123/track/not-a-guid", out _, out _));
    }
}