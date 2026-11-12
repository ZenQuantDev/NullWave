using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Time.Testing;
using NullWave.Models;
using NullWave.Services;
using Xunit;

namespace NullWave.Tests;

public class SmartPlaylistServiceTests
{
    // 26 Oct 2026 in UTC
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
    private static FakeTimeProvider Clock() => new(Now);

    private static Track MakeTrack(string title, int playCount, DateTime? lastPlayed, 
        MediaType mediaType = MediaType.Music) =>
        new() { Title = title, PlayCount = playCount, LastPlayed = lastPlayed, MediaType = mediaType };

    private SmartPlaylistService CreateService(TimeProvider? clock = null)
    {
        return new SmartPlaylistService(null!, clock ?? TimeProvider.System);
    }

    [Fact]
    public void RecentlyAdded_excludes_radio_and_audiobooks()
    {
        var tracks = new List<Track>
        {
            MakeTrack("Music", 0, null, MediaType.Music),
            MakeTrack("Radio", 0, null, MediaType.Radio),
            MakeTrack("Audiobook", 0, null, MediaType.Audiobook)
        };
        var service = CreateService();
        var result = service.ResolveInternal(SmartPlaylistService.RecentlyAdded, tracks);
        Assert.Single(result);
        Assert.Equal("Music", result[0].Title);
    }

    [Fact]
    public void MostPlayed_requires_at_least_one_play()
    {
        var tracks = new List<Track>
        {
            MakeTrack("Never", 0, null),
            MakeTrack("Once", 1, null),
            MakeTrack("Twice", 2, null)
        };
        var service = CreateService();
        var result = service.ResolveInternal(SmartPlaylistService.MostPlayed, tracks);
        Assert.Equal(2, result.Count);
        Assert.DoesNotContain(result, t => t.Title == "Never");
    }

    [Fact]
    public void ForgottenFavorites_respects_90_day_boundary()
    {
        var clock = Clock();
        var tracks = new List<Track>
        {
            MakeTrack("Recent", 5, DateTime.Parse("2026-10-01T12:00:00Z")),
            MakeTrack("Old", 5, DateTime.Parse("2026-07-01T12:00:00Z")),
            MakeTrack("Exactly90Days", 5, DateTime.Parse("2026-07-28T12:00:00Z"))
        };
        var service = CreateService(clock);
        var result = service.ResolveInternal(service.ForgottenFavorites, tracks);
        Assert.Single(result);
        Assert.Equal("Old", result[0].Title);
    }

    [Fact]
    public void ForgottenFavorites_requires_at_least_two_plays()
    {
        var clock = Clock();
        var tracks = new List<Track>
        {
            MakeTrack("Once", 1, DateTime.Parse("2026-01-01T12:00:00Z")),
            MakeTrack("Twice", 2, DateTime.Parse("2026-01-01T12:00:00Z"))
        };
        var service = CreateService(clock);
        var result = service.ResolveInternal(service.ForgottenFavorites, tracks);
        Assert.Single(result);
        Assert.Equal("Twice", result[0].Title);
    }
}