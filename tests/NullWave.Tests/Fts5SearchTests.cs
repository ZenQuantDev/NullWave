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
        var id = database.LoadAll().Single(t => t.Title == "Daft Punk").Id;

        Assert.True(database.HasFtsIndex());
        Assert.Contains(id, database.SearchFts("Daft")!);
    }

    [Fact]
    public void Fts_search_supports_prefix_and_multi_word_matching()
    {
        var bohemian = NewTrack("Bohemian Rhapsody", "Queen");
        var anotherQueenTrack = NewTrack("Killer Queen", "Queen");
        _database.Insert(bohemian);
        _database.Insert(anotherQueenTrack);

        var prefixResults = _database.SearchFts("Bohe")!;
        Assert.Contains(bohemian.Id, prefixResults);

        // Multi-word uses AND logic now
        var andResults = _database.SearchFts("Bohemian Rhapsody")!;
        Assert.Contains(bohemian.Id, andResults);
        Assert.DoesNotContain(anotherQueenTrack.Id, andResults);
        
        // Single word still matches both
        var queenResults = _database.SearchFts("Queen")!;
        Assert.Contains(bohemian.Id, queenResults);
        Assert.Contains(anotherQueenTrack.Id, queenResults);
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
        
        // Punctuation is sanitized to spaces. "Oper\"* OR" becomes "Oper OR".
        // With AND logic, it requires BOTH words. The track doesn't have "OR", so it shouldn't match.
        Assert.DoesNotContain(track.Id, _database.SearchFts("Oper\"* OR")!);
        
        // Searching for two words that ARE in the album should match (AND logic)
        Assert.Contains(track.Id, _database.SearchFts("Night Oper")!);
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

    [Fact]
    public void Fts_search_two_words_uses_AND_logic()
    {
        var both = NewTrack("Pastel Ghost", "Artist A");
        var onlyFirst = NewTrack("Pastel", "Artist B");
        var onlySecond = NewTrack("Ghost", "Artist C");
        var neither = NewTrack("Something Else", "Artist D");

        _database.Insert(both);
        _database.Insert(onlyFirst);
        _database.Insert(onlySecond);
        _database.Insert(neither);

        // Two-word query should require BOTH words (AND logic)
        var twoWordResults = _database.SearchFts("pastel ghost")!;
        Assert.Contains(both.Id, twoWordResults);
        Assert.DoesNotContain(onlyFirst.Id, twoWordResults);
        Assert.DoesNotContain(onlySecond.Id, twoWordResults);
        Assert.DoesNotContain(neither.Id, twoWordResults);

        // One-word query should still prefix-match
        var oneWordResults = _database.SearchFts("pastel")!;
        Assert.Contains(both.Id, oneWordResults);
        Assert.Contains(onlyFirst.Id, oneWordResults);
        Assert.DoesNotContain(onlySecond.Id, oneWordResults);
        Assert.DoesNotContain(neither.Id, oneWordResults);
    }
}