using System;
using System.Collections.Generic;
using System.IO;
using NullWave.Helpers;
using NullWave.Models;
using Xunit;

namespace NullWave.Tests;

public class TrackRecordTests
{
    private const string GapReason = "Known gap in TrackRecord - remove Skip after applying the fix";

    private static string OutsideData(params string[] parts) =>
        Path.Combine(Path.GetTempPath(), Path.Combine(parts));

    [Fact]
    public void Round_trip_keeps_every_field()
    {
        var original = new Track
        {
            Title = "Let It Happen", Artist = "Tame Impala", Url = "https://youtu.be/abc123",
            FilePath = OutsideData("music", "let-it-happen.mp3"),
            AlbumArtPath = OutsideData("art", "currents.jpg"),
            Source = TrackSource.YouTube,
            DateAdded = new DateTime(2026, 9, 3, 12, 0, 0, DateTimeKind.Utc),
            IsFavorite = true, PlayCount = 6, SkipCount = 2,
            LastPlayed = new DateTime(2026, 9, 19, 8, 30, 0, DateTimeKind.Utc),
            LastSkipped = new DateTime(2026, 9, 18, 7, 0, 0, DateTimeKind.Utc),
            Tags = new() { "indie", "psychedelic" },
            Album = "Currents", TrackNumber = 1, Notes = "great intro",
            Duration = TimeSpan.FromSeconds(467),
            MediaType = MediaType.Audiobook, PlaybackPositionTicks = 12345, TitleForceCleaned = true
        };

        var copy = TrackRecord.FromTrack(original).ToTrack();

        Assert.Equal(original.Id, copy.Id);
        Assert.Equal(original.Title, copy.Title);
        Assert.Equal(original.Artist, copy.Artist);
        Assert.Equal(original.Url, copy.Url);
        Assert.Equal(original.FilePath, copy.FilePath);
        Assert.Equal(original.AlbumArtPath, copy.AlbumArtPath);
        Assert.Equal(original.Source, copy.Source);
        Assert.Equal(original.DateAdded, copy.DateAdded);
        Assert.Equal(original.IsFavorite, copy.IsFavorite);
        Assert.Equal(original.PlayCount, copy.PlayCount);
        Assert.Equal(original.SkipCount, copy.SkipCount);
        Assert.Equal(original.LastPlayed, copy.LastPlayed);
        Assert.Equal(original.LastSkipped, copy.LastSkipped);
        Assert.Equal(original.Tags, copy.Tags);
        Assert.Equal(original.Album, copy.Album);
        Assert.Equal(original.TrackNumber, copy.TrackNumber);
        Assert.Equal(original.Notes, copy.Notes);
        Assert.Equal(original.Duration, copy.Duration);
        Assert.Equal(original.MediaType, copy.MediaType);
        Assert.Equal(original.PlaybackPositionTicks, copy.PlaybackPositionTicks);
        Assert.Equal(original.TitleForceCleaned, copy.TitleForceCleaned);
    }

    [Fact]
    public void Unknown_values_in_the_database_fall_back_to_safe_defaults()
    {
        var row = new TrackRecord
        {
            Id = "not-a-guid", Title = "T", Artist = "A",
            Source = "Bogus", MediaType = "Bogus", TagsRaw = "a||b|"
        };

        var track = row.ToTrack();

        Assert.NotEqual(Guid.Empty, track.Id);
        Assert.Equal(TrackSource.Unknown, track.Source);
        Assert.Equal(MediaType.Music, track.MediaType);
        Assert.Equal(new List<string> { "a", "b" }, track.Tags);
    }

    [Fact]
    public void Tracks_without_tags_store_null_and_load_as_an_empty_list()
    {
        var row = TrackRecord.FromTrack(new Track { Title = "T", Artist = "A" });

        Assert.Null(row.TagsRaw);
        Assert.Empty(row.ToTrack().Tags);
    }

    [Fact]
    public void File_paths_inside_the_data_folder_are_stored_as_portable_tokens()
    {
        var path = Path.Combine(NullWavePaths.DataDir, "downloads", "song.mp3");

        var row = TrackRecord.FromTrack(new Track { Title = "T", Artist = "A", FilePath = path });

        Assert.StartsWith(PathHelper.DataToken + "/", row.FilePath);
        Assert.Equal(path, row.ToTrack().FilePath);
    }

    [Fact]
    public void Tags_containing_a_pipe_do_not_split_into_extra_tags()
    {
        var original = new Track { Title = "T", Artist = "A", Tags = new() { "r&b | soul", "pop" } };

        var copy = TrackRecord.FromTrack(original).ToTrack();

        Assert.Equal(original.Tags.Count, copy.Tags.Count);
    }
}

public class PathHelperTests
{
    private const string GapReason = "Known gap in PathHelper - remove Skip after applying the fix";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_values_pass_through_unchanged(string? value)
    {
        Assert.Equal(value, PathHelper.Tokenize(value));
        Assert.Equal(value, PathHelper.Resolve(value));
    }

    [Fact]
    public void Paths_outside_the_data_folder_stay_absolute()
    {
        var path = Path.Combine(Path.GetTempPath(), "Music", "song.mp3");

        Assert.Equal(path, PathHelper.Tokenize(path));
        Assert.Equal(path, PathHelper.Resolve(path));
    }

    [Fact]
    public void Paths_inside_the_data_folder_become_tokens_and_resolve_back()
    {
        var path = Path.Combine(NullWavePaths.DataDir, "art", "cover.jpg");

        var token = PathHelper.Tokenize(path);

        Assert.Equal("<NW_DATA>/art/cover.jpg", token);
        Assert.Equal(path, PathHelper.Resolve(token));
    }

    [Fact]
    public void A_sibling_folder_with_a_similar_name_is_not_treated_as_the_data_folder()
    {
        var path = NullWavePaths.DataDir + "-old" + Path.DirectorySeparatorChar + "song.mp3";

        Assert.Equal(path, PathHelper.Tokenize(path));
    }
}

public class PlaylistTests
{
    [Fact]
    public void GetDateAdded_uses_the_stored_date_and_falls_back_to_the_creation_date()
    {
        var created = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var added = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var playlist = new Playlist { DateCreated = created };
        var stored = Guid.NewGuid();
        var unset = Guid.NewGuid();
        playlist.TrackDateAdded[stored] = added;
        playlist.TrackDateAdded[unset] = default;

        Assert.Equal(added, playlist.GetDateAdded(stored));
        Assert.Equal(created, playlist.GetDateAdded(unset));
        Assert.Equal(created, playlist.GetDateAdded(Guid.NewGuid()));
    }

    [Fact]
    public void ArtPath_prefers_custom_art_then_the_first_tracks_art()
    {
        var playlist = new Playlist();
        Assert.Null(playlist.ArtPath);

        playlist.Tracks.Add(new Track { AlbumArtPath = "first.jpg" });
        Assert.Equal("first.jpg", playlist.ArtPath);

        playlist.CustomArtPath = "custom.png";
        Assert.Equal("custom.png", playlist.ArtPath);
    }

    [Fact]
    public void Changing_custom_art_also_notifies_ArtPath()
    {
        var playlist = new Playlist();
        var raised = new List<string?>();
        playlist.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        playlist.CustomArtPath = "custom.png";

        Assert.Contains(nameof(Playlist.CustomArtPath), raised);
        Assert.Contains(nameof(Playlist.ArtPath), raised);
    }
}