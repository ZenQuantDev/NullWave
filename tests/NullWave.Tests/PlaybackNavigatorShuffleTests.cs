using System;
using System.Collections.Generic;
using System.Linq;
using NullWave.Models;
using NullWave.Services;
using Xunit;

namespace NullWave.Tests;

public class PlaybackNavigatorShuffleTests
{
    private static PlaybackNavigator ShuffleNavigator(List<Track> library, int seed = 1)
    {
        var nav = NavTest.Navigator(library, new Random(seed));
        nav.IsShuffle = true;
        return nav;
    }

    private static int DistinctCount(IEnumerable<Track> tracks) => tracks.Distinct().Count();

    // ---------- Shuffle deck ----------

    [Fact]
    public void Upcoming_shuffle_contains_every_track_exactly_once()
    {
        var lib = NavTest.Music(30);
        var upcoming = ShuffleNavigator(lib).GenerateUpcoming(30);

        Assert.Equal(30, upcoming.Count);
        Assert.Equal(30, DistinctCount(upcoming));
    }

    [Fact]
    public void Upcoming_shuffle_leaves_out_the_track_that_is_playing()
    {
        var lib = NavTest.Music(30);
        var nav = ShuffleNavigator(lib, seed: 2);
        nav.CurrentTrack = lib[5];

        var upcoming = nav.GenerateUpcoming(29);

        Assert.Equal(29, DistinctCount(upcoming));
        Assert.DoesNotContain(lib[5], upcoming);
    }

    [Fact]
    public void Playing_through_a_full_pass_visits_every_track_once_and_never_repeats_back_to_back()
    {
        var lib = NavTest.Music(30);
        var nav = ShuffleNavigator(lib, seed: 3);

        var played = new List<Track>();
        Track? current = null;
        for (var i = 0; i < 30; i++)
        {
            current = nav.GetNextTrack(current);
            played.Add(current!);
        }

        Assert.Equal(30, DistinctCount(played));
        Assert.NotSame(played[^1], nav.GetNextTrack(current));   // new deck starts with a different track
    }

    [Fact]
    public void Previewing_upcoming_tracks_consumes_them_from_the_same_deck()
    {
        var lib = NavTest.Music(30);
        var nav = ShuffleNavigator(lib, seed: 8);

        var previewed = nav.GenerateUpcoming(3);
        var next = nav.GetNextTrack(null);

        Assert.DoesNotContain(next, previewed);
    }

    // ---------- Skip penalty ----------

    [Fact]
    public void Tracks_at_the_skip_cap_are_left_out_when_enough_others_remain()
    {
        var lib = NavTest.Music(30);
        foreach (var track in lib.Take(5)) track.SkipCount = 3;

        var upcoming = ShuffleNavigator(lib, seed: 4).GenerateUpcoming(25);

        Assert.Equal(25, DistinctCount(upcoming));
        Assert.All(upcoming, t => Assert.True(t.SkipCount < 3));
    }

    [Fact]
    public void The_skip_penalty_is_ignored_when_fewer_than_20_tracks_would_remain()
    {
        var lib = NavTest.Music(30);
        foreach (var track in lib.Take(15)) track.SkipCount = 3;

        var upcoming = ShuffleNavigator(lib, seed: 5).GenerateUpcoming(30);

        Assert.Equal(30, DistinctCount(upcoming));
    }

    [Fact]
    public void Small_libraries_keep_penalised_tracks()
    {
        var lib = NavTest.Music(10);
        lib[0].SkipCount = 5;

        var upcoming = ShuffleNavigator(lib, seed: 7).GenerateUpcoming(10);

        Assert.Equal(10, DistinctCount(upcoming));
        Assert.Contains(lib[0], upcoming);
    }

    [Fact]
    public void A_skip_cap_of_zero_turns_the_penalty_off()
    {
        var lib = NavTest.Music(30);
        foreach (var track in lib) track.SkipCount = 5;
        var nav = ShuffleNavigator(lib, seed: 6);
        nav.SkipPenaltyCap = 0;

        Assert.Equal(30, DistinctCount(nav.GenerateUpcoming(30)));
    }

    // ---------- Smart shuffle (Random is fixed so results are predictable) ----------

    private static (PlaybackNavigator Nav, List<Track> Lib, Track Current, Track SameArtist, Track SameTag, Track Favorite)
        SmartSetup()
    {
        var current  = new Track { Title = "current", Artist = "X", Tags = new() { "rock" } };
        var sameArtist = new Track { Title = "same artist", Artist = "X", Tags = new() { "rock" } };  // score 7
        var sameTag  = new Track { Title = "same tag", Artist = "Y", Tags = new() { "rock" } };       // score 2
        var favorite = new Track { Title = "favorite", Artist = "Z", IsFavorite = true };             // score 1
        var lib = new List<Track> { current, sameArtist, sameTag, favorite };

        var nav = NavTest.Navigator(lib, new NavTest.FixedRandom(0.0));   // 0.0 always picks the smart branch
        nav.IsShuffle = true;
        nav.IsSmartShuffle = true;
        return (nav, lib, current, sameArtist, sameTag, favorite);
    }

    [Fact]
    public void Smart_shuffle_prefers_same_artist_and_shared_tags()
    {
        var s = SmartSetup();

        Assert.Same(s.SameArtist, s.Nav.GetNextTrack(s.Current));
    }

    [Fact]
    public void Smart_shuffle_skips_recently_played_tracks()
    {
        var s = SmartSetup();
        s.Nav.RecordPlay(s.SameArtist);

        Assert.Same(s.SameTag, s.Nav.GetNextTrack(s.Current));
    }

    [Fact]
    public void Smart_shuffle_skips_tracks_at_the_skip_cap()
    {
        var s = SmartSetup();
        s.SameArtist.SkipCount = 3;

        Assert.Same(s.SameTag, s.Nav.GetNextTrack(s.Current));
    }
}