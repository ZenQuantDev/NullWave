using System.IO;
using System.Linq;
using NullWave.Models;
using NullWave.Services;
using Xunit;

namespace NullWave.Tests;

public class PlaybackNavigatorSequentialTests
{
    private const string GapReason = "Known gap in PlaybackNavigator - remove Skip after applying the fix";

    // ---------- Next / previous in library order ----------

    [Fact]
    public void Next_starts_at_the_first_track_when_nothing_is_playing()
    {
        var lib = NavTest.Music(5);
        var nav = NavTest.Navigator(lib);

        Assert.Same(lib[0], nav.GetNextTrack(null));
        Assert.Same(lib[0], nav.CurrentTrack);
    }

    [Fact]
    public void Next_walks_the_library_in_order()
    {
        var lib = NavTest.Music(5);
        var nav = NavTest.Navigator(lib);

        for (var i = 0; i < 4; i++)
            Assert.Same(lib[i + 1], nav.GetNextTrack(lib[i]));
    }

    [Fact]
    public void Next_at_the_end_stops_when_repeat_is_off()
    {
        var lib = NavTest.Music(3);
        var nav = NavTest.Navigator(lib);

        Assert.Null(nav.GetNextTrack(lib[^1]));
        Assert.Null(nav.CurrentTrack);
    }

    [Fact]
    public void Next_at_the_end_wraps_with_repeat_all()
    {
        var lib = NavTest.Music(3);
        var nav = NavTest.Navigator(lib);
        nav.RepeatMode = RepeatMode.All;

        Assert.Same(lib[0], nav.GetNextTrack(lib[^1]));
    }

    [Fact]
    public void Next_returns_null_for_an_empty_library()
        => Assert.Null(NavTest.Navigator(NavTest.Music(0)).GetNextTrack(null));

    [Fact]
    public void Previous_walks_back_stops_at_the_first_track_and_wraps_with_repeat_all()
    {
        var lib = NavTest.Music(5);
        var nav = NavTest.Navigator(lib);

        Assert.Same(lib[1], nav.GetPreviousTrack(lib[2]));
        Assert.Null(nav.GetPreviousTrack(lib[0]));

        nav.RepeatMode = RepeatMode.All;
        Assert.Same(lib[4], nav.GetPreviousTrack(lib[0]));
    }

    [Fact]
    public void CycleRepeat_goes_none_all_one_none()
    {
        var nav = NavTest.Navigator(NavTest.Music(1));
        Assert.Equal(RepeatMode.None, nav.RepeatMode);

        nav.CycleRepeat();
        Assert.Equal(RepeatMode.All, nav.RepeatMode);
        Assert.False(nav.ShouldRepeatCurrent());

        nav.CycleRepeat();
        Assert.Equal(RepeatMode.One, nav.RepeatMode);
        Assert.True(nav.ShouldRepeatCurrent());

        nav.CycleRepeat();
        Assert.Equal(RepeatMode.None, nav.RepeatMode);
    }

    // ---------- Upcoming (queue preview) ----------

    [Fact]
    public void Upcoming_lists_the_tracks_after_the_current_one_and_stops_at_the_end()
    {
        var lib = NavTest.Music(5);
        var nav = NavTest.Navigator(lib);

        nav.CurrentTrack = lib[1];
        Assert.Equal(new[] { lib[2], lib[3] }, nav.GenerateUpcoming(2));

        nav.CurrentTrack = lib[3];
        Assert.Equal(new[] { lib[4] }, nav.GenerateUpcoming(5));
    }

    // ---------- History-based Previous ----------

    [Fact]
    public void Previous_jumps_back_to_the_last_played_track_and_then_uses_library_order()
    {
        var lib = NavTest.Music(10);
        var nav = NavTest.Navigator(lib);
        nav.RecordPlay(lib[7]);

        Assert.Same(lib[7], nav.GetPreviousTrack(lib[3]));
        Assert.Same(lib[6], nav.GetPreviousTrack(lib[7]));   // history is used up
    }

    [Fact]
    public void History_keeps_only_the_last_50_plays()
    {
        var lib = NavTest.Music(60);
        var nav = NavTest.Navigator(lib);
        foreach (var track in lib) nav.RecordPlay(track);

        var jumps = 0;
        Track? last = null;
        while (nav.GetPreviousTrack(lib[0]) is { } previous)
        {
            jumps++;
            last = previous;
        }

        Assert.Equal(50, jumps);
        Assert.Same(lib[10], last);   // tracks 0-9 fell out of the history
    }

    [Fact]
    public void Previous_never_returns_the_track_that_is_already_playing()
    {
        var lib = NavTest.Music(6);
        var nav = NavTest.Navigator(lib);
        nav.RecordPlay(lib[2]);

        Assert.Same(lib[2], nav.GetPreviousTrack(lib[3]));   // history and neighbour agree
        Assert.Same(lib[1], nav.GetPreviousTrack(lib[2]));   // today this returns lib[2] again
    }

    [Fact]
    public void Upcoming_wraps_around_with_repeat_all()
    {
        var lib = NavTest.Music(3);
        var nav = NavTest.Navigator(lib);
        nav.CurrentTrack = lib[2];
        nav.RepeatMode = RepeatMode.All;

        Assert.Equal(new[] { lib[0], lib[1] }, nav.GenerateUpcoming(2));
    }

    // ---------- Music, audiobooks and chapters stay separate ----------

    [Fact]
    public void Music_navigation_never_returns_audiobooks()
    {
        var music = NavTest.Music(3);
        var lib = music.Concat(new[] { NavTest.Audiobook("a0"), NavTest.Audiobook("a1") }).ToList();
        var nav = NavTest.Navigator(lib);

        Assert.Same(music[1], nav.GetNextTrack(music[0]));
        Assert.Null(nav.GetNextTrack(music[2]));

        nav.CurrentTrack = music[0];
        Assert.Equal(new[] { music[1], music[2] }, nav.GenerateUpcoming(10));
    }

    [Fact]
    public void Audiobook_navigation_stays_inside_the_same_book()
    {
        var c1 = NavTest.Audiobook("Chapter 1", album: "Book 1");
        var other = NavTest.Audiobook("Chapter 1", album: "Book 2");
        var c2 = NavTest.Audiobook("Chapter 2", album: "Book 1");
        var nav = NavTest.Navigator(new() { c1, other, c2 });

        Assert.Same(c2, nav.GetNextTrack(c1));
        Assert.Null(nav.GetNextTrack(c2));
    }

    [Fact]
    public void Audiobooks_without_an_album_are_grouped_by_folder()
    {
        var dirA = Path.Combine(Path.GetTempPath(), "bookA");
        var dirB = Path.Combine(Path.GetTempPath(), "bookB");
        var a1 = NavTest.Audiobook("1", artist: "", path: Path.Combine(dirA, "1.mp3"));
        var b1 = NavTest.Audiobook("1", artist: "", path: Path.Combine(dirB, "1.mp3"));
        var a2 = NavTest.Audiobook("2", artist: "", path: Path.Combine(dirA, "2.mp3"));
        var nav = NavTest.Navigator(new() { a1, b1, a2 });

        Assert.Same(a2, nav.GetNextTrack(a1));
        Assert.Null(nav.GetNextTrack(a2));
    }

    [Fact]
    public void Chapters_are_ordered_by_track_number_then_title()
    {
        var c3 = NavTest.Audiobook("C", album: "Book", number: 3);
        var c1 = NavTest.Audiobook("A", album: "Book", number: 1);
        var c2 = NavTest.Audiobook("B", album: "Book", number: 2);
        var nav = NavTest.Navigator(new() { c3, c1, c2 });

        Assert.Same(c2, nav.GetNextChapter(c1));
        Assert.Same(c3, nav.GetNextChapter(c2));
        Assert.Null(nav.GetNextChapter(c3));
        Assert.Same(c1, nav.GetPreviousChapter(c2));
        Assert.Null(nav.GetPreviousChapter(c1));
    }

    [Fact]
    public void Chapters_need_an_album()
    {
        var loose = NavTest.Audiobook("Loose", album: null);
        var nav = NavTest.Navigator(new() { loose });

        Assert.Null(nav.GetNextChapter(loose));
        Assert.Null(nav.GetPreviousChapter(loose));
    }
}