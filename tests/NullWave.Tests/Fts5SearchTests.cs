using System;
using System.IO;
using System.Linq;
using NullWave.Helpers;
using NullWave.Models;
using NullWave.Services;
using SQLite;
using Xunit;

namespace NullWave.Tests;

[Collection("Database")]
public sealed class Fts5SearchTests : IDisposable
{
    private readonly string _dbPath = NullWavePaths.DatabasePath;
    private readonly DatabaseService _database;

    public Fts5SearchTests()
    {
        if (!NullWavePaths.DataDir.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing to run: data folder is not a temp folder.");

        DeleteDatabaseFiles();
        _database = new DatabaseService();
    }

    public void Dispose()
    {
        _database.Dispose();
        DeleteDatabaseFiles();
    }

    [Fact]
    public void Fts_index_is_created_and_existing_tracks_are_backfilled()
    {
        using (var connection = new SQLiteConnection(_dbPath))
        {
            connection.CreateTable<TrackRecord>();
            connection.Insert(new TrackRecord
            {
                Id = Guid.NewGuid().ToString(),
                Title = "Daft Punk",
                Artist = "Around the World"
            });
        }

        using var database = new DatabaseService();
        var id = database.LoadAll().Single().Id;

        Assert.True(database.HasFtsIndex());
        Assert.Contains(id, database.SearchFts("Daft")!);
    }

    [Fact]
    public void Fts_search_supports_prefix_and_or_matching()
    {
        var bohemian = NewTrack("Bohemian Rhapsody", "Queen");
        var anotherQueenTrack = NewTrack("Killer Queen", "Queen");
        _database.Insert(bohemian);
        _database.Insert(anotherQueenTrack);

        var prefixResults = _database.SearchFts("Bohe")!;
        var orResults = _database.SearchFts("Rhapsody Killer")!;

        Assert.Contains(bohemian.Id, prefixResults);
        Assert.Contains(bohemian.Id, orResults);
        Assert.Contains(anotherQueenTrack.Id, orResults);
    }

    [Fact]
    public void Fts_searches_album_and_tags_and_sanitizes_punctuation()
    {
        var track = new Track
        {
            Id = Guid.NewGuid(),
            Title = "Test Track",
            Artist = "Test Artist",
            Album = "Night at the Opera",
            Tags = new() { "Progressive Rock" }
        };
        _database.Insert(track);

        Assert.Contains(track.Id, _database.SearchFts("Oper")!);
        Assert.Contains(track.Id, _database.SearchFts("Progress")!);
        Assert.Contains(track.Id, _database.SearchFts("Oper\"* OR")!);
    }

    [Fact]
    public void Fts_triggers_follow_insert_replace_update_and_delete()
    {
        var track = NewTrack("OriginalTitle", "InitialArtist");
        _database.Insert(track);
        Assert.Contains(track.Id, _database.SearchFts("Original")!);

        track.Title = "ReplacedTitle";
        _database.Insert(track);
        Assert.DoesNotContain(track.Id, _database.SearchFts("Original")!);
        Assert.Contains(track.Id, _database.SearchFts("Replaced")!);

        track.Artist = "UpdatedArtist";
        _database.Update(track);
        Assert.DoesNotContain(track.Id, _database.SearchFts("Original")!);
        Assert.Contains(track.Id, _database.SearchFts("Updated")!);

        _database.Delete(track.Id);
        Assert.DoesNotContain(track.Id, _database.SearchFts("Replaced")!);
        Assert.DoesNotContain(track.Id, _database.SearchFts("Updated")!);
    }

    [Fact]
    public void Rebuild_repopulates_the_search_index()
    {
        var track = NewTrack("RebuildTarget", "Test Artist");
        _database.Insert(track);

        _database.RebuildFtsIndex();

        Assert.Contains(track.Id, _database.SearchFts("Rebuild")!);
    }

    private static Track NewTrack(string title, string artist) => new()
    {
        Id = Guid.NewGuid(),
        Title = title,
        Artist = artist
    };

    private void DeleteDatabaseFiles()
    {
        var directory = Path.GetDirectoryName(_dbPath)!;
        foreach (var file in Directory.GetFiles(directory, "library.db*"))
        {
            try { File.Delete(file); }
            catch { }
        }
    }
}