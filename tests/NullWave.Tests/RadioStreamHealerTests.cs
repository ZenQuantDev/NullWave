using System;
using System.Collections.Generic;
using NullWave.Models;
using NullWave.Plugins.Contracts;
using NullWave.Services.Radio;

namespace NullWave.Tests;

public class RadioStreamHealerTests
{
    private static Track RadioTrack(string? url, params string[] tags) => new()
    {
        Id = Guid.NewGuid(),
        Title = "Test FM",
        MediaType = MediaType.Radio,
        Url = url,
        Tags = new List<string>(tags)
    };

    [Fact]
    public void Untagged_station_is_skipped()
    {
        var preview = RadioStreamHealer.Classify(RadioTrack("http://a"), null);

        Assert.Equal(RadioHealStatus.Skipped, preview.Status);
    }

    [Fact]
    public void Missing_from_directory_is_dead()
    {
        var preview = RadioStreamHealer.Classify(
            RadioTrack("http://a", "radiobrowser:uuid-1"), null);

        Assert.Equal(RadioHealStatus.Dead, preview.Status);
    }

    [Fact]
    public void Same_url_is_healthy()
    {
        var station = new RadioStationInfo("uuid-1", "Test FM", null, null, null, "http://a", null);

        var preview = RadioStreamHealer.Classify(
            RadioTrack("http://a", "radiobrowser:uuid-1"), station);

        Assert.Equal(RadioHealStatus.Healthy, preview.Status);
        Assert.Null(preview.NewUrl);
    }

    [Fact]
    public void Changed_url_is_moved_with_new_url()
    {
        var station = new RadioStationInfo("uuid-1", "Test FM", null, null, null, "http://b", null);

        var preview = RadioStreamHealer.Classify(
            RadioTrack("http://a", "radiobrowser:uuid-1"), station);

        Assert.Equal(RadioHealStatus.Moved, preview.Status);
        Assert.Equal("http://b", preview.NewUrl);
    }

    [Fact]
    public void Empty_stream_url_is_dead()
    {
        var station = new RadioStationInfo("uuid-1", "Test FM", null, null, null, "  ", null);

        var preview = RadioStreamHealer.Classify(
            RadioTrack("http://a", "radiobrowser:uuid-1"), station);

        Assert.Equal(RadioHealStatus.Dead, preview.Status);
    }
}
