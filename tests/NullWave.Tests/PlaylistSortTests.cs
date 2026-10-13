using System;
using System.Collections.Generic;
using System.Linq;
using NullWave.Models;
using NullWave.ViewModels;
using Xunit;

namespace NullWave.Tests;

/// <summary>
/// Phase 1.3 & 1.4: every sort option offered on a page must actually change the order.
/// Exercises the pure <see cref="PlaylistViewModel.SortTracks"/> helper, so no database is needed.
/// </summary>
public sealed class PlaylistSortTests
{
    [Fact]
    public void Every_playlist_sort_option_produces_its_expected_order()
    {
        var playlist = BuildPlaylist();

        var expectedAscending = new Dictionary<SortField, string[]>
        {
            [SortField.Custom]     = new[] { "Charlie", "Alpha", "Delta", "Bravo" },
            [SortField.Title]      = new[] { "Alpha", "Bravo", "Charlie", "Delta" },
            [SortField.Artist]     = new[] { "Charlie", "Bravo", "Delta", "Alpha" },
            [SortField.DateAdded]  = new[] { "Delta", "Charlie", "Bravo", "Alpha" },
            [SortField.Source]     = new[] { "Bravo", "Delta", "Alpha", "Charlie" },
            [SortField.PlayCount]  = new[] { "Alpha", "Charlie", "Bravo", "Delta" },
            [SortField.LastPlayed] = new[] { "Delta", "Alpha", "Charlie", "Bravo" },
            [SortField.Length]     = new[] { "Bravo", "Charlie", "Alpha", "Delta" } // Delta is Zero, sinks to bottom
        };

        var expectedDescending = new Dictionary<SortField, string[]>
        {
            [SortField.Custom]     = new[] { "Charlie", "Alpha", "Delta", "Bravo" },
            [SortField.Title]      = new[] { "Delta", "Charlie", "Bravo", "Alpha" },
            [SortField.Artist]     = new[] { "Alpha", "Delta", "Bravo", "Charlie" },
            [SortField.DateAdded]  = new[] { "Alpha", "Bravo", "Charlie", "Delta" },
            [SortField.Source]     = new[] { "Charlie", "Alpha", "Delta", "Bravo" },
            [SortField.PlayCount]  = new[] { "Delta", "Bravo", "Charlie", "Alpha" },
            [SortField.LastPlayed] = new[] { "Bravo", "Charlie", "Alpha", "Delta" },
            [SortField.Length]     = new[] { "Alpha", "Charlie", "Bravo", "Delta" } // Delta is Zero, sinks to bottom
        };

        foreach (var field in SortFieldCatalog.Playlist)
        {
            Assert.Equal(expectedAscending[field], Titles(Sort(playlist, field, ascending: true)));
            Assert.Equal(expectedDescending[field], Titles(Sort(playlist, field, ascending: false)));
        }
    }

    [Fact]
    public void No_playlist_sort_option_is_a_no_op()
    {
        var playlist = BuildPlaylist();

        var orders = SortFieldCatalog.Playlist
            .Select(field => string.Join("|", Titles(Sort(playlist, field, ascending: true))))
            .ToList();

        Assert.Equal(orders.Count, orders.Distinct().Count());
    }

    [Fact]
    public void Custom_sort_ignores_direction_and_preserves_manual_order()
    {
        var playlist = BuildPlaylist();
        var manual = new[] { "Charlie", "Alpha", "Delta", "Bravo" };

        Assert.Equal(manual, Titles(Sort(playlist, SortField.Custom, ascending: true)));
        Assert.Equal(manual, Titles(Sort(playlist, SortField.Custom, ascending: false)));
    }

    [Fact]
    public void DateAdded_sort_uses_the_per_playlist_date_not_the_global_one()
    {
        var playlist = BuildPlaylist();

        Assert.Equal(
            new[] { "Delta", "Charlie", "Bravo", "Alpha" },
            Titles(Sort(playlist, SortField.DateAdded, ascending: true)));
    }

    [Fact]
    public void Never_played_tracks_sort_first_ascending_and_last_descending()
    {
        var playlist = BuildPlaylist();

        Assert.Equal("Delta", Sort(playlist, SortField.LastPlayed, ascending: true).First().Title);
        Assert.Equal("Delta", Sort(playlist, SortField.LastPlayed, ascending: false).Last().Title);
    }

    [Fact]
    public void Length_sort_sinks_zero_durations_to_the_bottom()
    {
        var playlist = BuildPlaylist();

        // Ascending: Bravo (2m), Charlie (3m), Alpha (4m), Delta (0m)
        Assert.Equal("Delta", Sort(playlist, SortField.Length, ascending: true).Last().Title);
        
        // Descending: Alpha (4m), Charlie (3m), Bravo (2m), Delta (0m)
        Assert.Equal("Delta", Sort(playlist, SortField.Length, ascending: false).Last().Title);
    }

    [Fact]
    public void Playlist_catalog_covers_every_sort_field()
    {
        foreach (var field in Enum.GetValues<SortField>())
            Assert.Contains(field, SortFieldCatalog.Playlist);
    }

    [Fact]
    public void Library_catalog_excludes_manual_order()
    {
        Assert.DoesNotContain(SortField.Custom, SortFieldCatalog.Library);
        Assert.False(SortFieldCatalog.IsAllowed("Library", SortField.Custom));
        Assert.True(SortFieldCatalog.IsAllowed("Playlists", SortField.Custom));
    }

    [Theory]
    [InlineData("Library", SortField.Custom, SortField.Title)]
    [InlineData("Radio", SortField.Custom, SortField.Title)]
    [InlineData("Audiobooks", SortField.Custom, SortField.Title)]
    [InlineData("Playlists", SortField.Custom, SortField.Custom)]
    [InlineData("Library", SortField.PlayCount, SortField.PlayCount)]
    [InlineData("Library", SortField.Length, SortField.Length)]
    public void Clamp_replaces_only_invalid_options(string page, SortField input, SortField expected)
        => Assert.Equal(expected, SortFieldCatalog.Clamp(page, input));

    // --- Fixture -----------------------------------------------------------

    private static IEnumerable<Track> Sort(Playlist playlist, SortField field, bool ascending)
        => PlaylistViewModel.SortTracks(playlist.Tracks, playlist, field, ascending);

    private static IEnumerable<string> Titles(IEnumerable<Track> tracks) => tracks.Select(t => t.Title);

    private static Playlist BuildPlaylist()
    {
        var playlist = new Playlist { Name = "Sort Fixture", DateCreated = Date(2026, 1, 1) };

        // Manual order the Custom sort must preserve: Charlie, Alpha, Delta, Bravo.
        Add(playlist, "Charlie", "Alpha", TrackSource.Local,      playCount: 20, lastPlayed: Date(2026, 3, 1), addedToPlaylist: Date(2026, 1, 2), duration: TimeSpan.FromMinutes(3));
        Add(playlist, "Alpha",   "Echo",  TrackSource.SoundCloud, playCount: 10, lastPlayed: Date(2026, 2, 1), addedToPlaylist: Date(2026, 1, 4), duration: TimeSpan.FromMinutes(4));
        Add(playlist, "Delta",   "Delta", TrackSource.Spotify,    playCount: 40, lastPlayed: null,             addedToPlaylist: Date(2026, 1, 1), duration: TimeSpan.Zero);
        Add(playlist, "Bravo",   "Bravo", TrackSource.YouTube,    playCount: 30, lastPlayed: Date(2026, 4, 1), addedToPlaylist: Date(2026, 1, 3), duration: TimeSpan.FromMinutes(2));

        return playlist;
    }

    private static void Add(Playlist playlist, string title, string artist, TrackSource source,
                            int playCount, DateTime? lastPlayed, DateTime addedToPlaylist, TimeSpan duration)
    {
        var track = new Track
        {
            Title = title,
            Artist = artist,
            Source = source,
            PlayCount = playCount,
            LastPlayed = lastPlayed,
            DateAdded = Date(2025, 1, 1),
            Duration = duration
        };

        playlist.Tracks.Add(track);
        playlist.TrackDateAdded[track.Id] = addedToPlaylist;
    }

    private static DateTime Date(int year, int month, int day) => new(year, month, day, 0, 0, 0, DateTimeKind.Utc);
}