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
public class DatabaseSafetyTests : IDisposable
{
    private readonly string _dbPath = NullWavePaths.DatabasePath;
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "nw-db-" + Guid.NewGuid().ToString("N"));

    public DatabaseSafetyTests()
    {
        if (!NullWavePaths.DataDir.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing to run: data folder is not a temp folder.");

        Directory.CreateDirectory(_tempDir);
        Cleanup();
    }

    public void Dispose()
    {
        Cleanup();
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private void Cleanup()
    {
        foreach (var file in Directory.GetFiles(Path.GetDirectoryName(_dbPath)!, "library.db*"))
            try { File.Delete(file); } catch { }
    }

    private void CreateDbWithTrack(string path, string title)
    {
        using var conn = new SQLiteConnection(path);
        conn.CreateTable<TrackRecord>();
        conn.Insert(new TrackRecord { Id = Guid.NewGuid().ToString(), Title = title, Artist = "A" });
    }

    [Fact]
    public void An_invalid_restore_file_is_rejected_and_the_current_database_is_kept()
    {
        CreateDbWithTrack(_dbPath, "Current");
        File.WriteAllText(_dbPath + ".restoring", "this is not a database");

        using var db = new DatabaseService();

        Assert.Equal("Current", db.LoadAll().Single().Title);
        Assert.False(File.Exists(_dbPath + ".restoring"));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(_dbPath)!, "library.db.restoring.rejected-*"));
    }

    [Fact]
    public void A_valid_restore_replaces_the_database_and_keeps_the_previous_one()
    {
        CreateDbWithTrack(_dbPath, "Current");
        CreateDbWithTrack(_dbPath + ".restoring", "Restored");

        using var db = new DatabaseService();

        Assert.Equal("Restored", db.LoadAll().Single().Title);
        Assert.True(DatabaseService.IsValidDatabase(_dbPath + ".pre-restore"));
    }

    private string Touch(string path, DateTime lastWrite)
    {
        File.WriteAllText(path, "x");
        File.SetLastWriteTime(path, lastWrite);
        return path;
    }

    [Fact]
    public void No_backup_yet_means_one_is_due()
    {
        var db = Touch(Path.Combine(_tempDir, "library.db"), new DateTime(2026, 9, 1));
        Assert.True(DatabaseService.BackupIsDue(db, _tempDir, new DateTime(2026, 9, 21, 9, 0, 0)));
    }

    [Fact]
    public void A_backup_under_24_hours_old_is_enough()
    {
        var db = Touch(Path.Combine(_tempDir, "library.db"), new DateTime(2026, 9, 21, 8, 0, 0));
        Touch(Path.Combine(_tempDir, "library-20260920-120000.db"), DateTime.Now);
        Assert.False(DatabaseService.BackupIsDue(db, _tempDir, new DateTime(2026, 9, 21, 9, 0, 0)));
    }

    [Fact]
    public void An_old_backup_is_not_replaced_if_the_database_did_not_change()
    {
        var db = Touch(Path.Combine(_tempDir, "library.db"), new DateTime(2026, 9, 1));
        Touch(Path.Combine(_tempDir, "library-20260915-120000.db"), DateTime.Now);
        Assert.False(DatabaseService.BackupIsDue(db, _tempDir, new DateTime(2026, 9, 21, 9, 0, 0)));
    }

    [Fact]
    public void An_old_backup_is_replaced_once_the_database_changed()
    {
        var db = Touch(Path.Combine(_tempDir, "library.db"), new DateTime(2026, 9, 20));
        Touch(Path.Combine(_tempDir, "library-20260915-120000.db"), DateTime.Now);
        Assert.True(DatabaseService.BackupIsDue(db, _tempDir, new DateTime(2026, 9, 21, 9, 0, 0)));
    }

    [Fact]
    public void Schema_version_advances_and_misclassified_radio_is_repaired()
    {
        // 1. Setup: create a raw DB with a misclassified track and an old schema version
        Guid trackId = Guid.NewGuid();
        using (var rawConn = new SQLiteConnection(_dbPath))
        {
            rawConn.CreateTable<TrackRecord>();
            rawConn.Insert(new TrackRecord
            {
                Id = trackId.ToString(),
                Title = "Radiohead - Creep",
                Artist = "Radiohead",
                Source = "YouTube",
                MediaType = "Radio" // Wrongly classified by the old buggy logic
            });
            // Simulate an old DB that hasn't run migration 8 yet
            rawConn.Execute("PRAGMA user_version = 7;");
        }

        // 2. Re-open via DatabaseService to trigger the repair migration
        using (var db = new DatabaseService())
        {
            Assert.True(db.SchemaVersion >= 8, $"Schema version should be >= 8, but was {db.SchemaVersion}");

            var loaded = db.LoadAll().FirstOrDefault(t => t.Id == trackId);
            Assert.NotNull(loaded);
            
            // 2.6 Verify the repair migration fixed it
            Assert.Equal(MediaType.Music, loaded.MediaType);
        }
    }

    [Fact]
    public async Task Schema_migrations_run_only_once_across_reopens()
    {
        // First open: runs migrations 1-8 synchronously, 9 asynchronously
        using (var db = new DatabaseService())
        {
            var v1 = db.SchemaVersion;
            Assert.True(v1 >= 8);
        }

        // Wait for the async migration 9 (path tokenization) to complete
        await Task.Delay(300);

        int v2;
        using (var db = new DatabaseService())
        {
            // Wait for migration 9 to complete on this open as well
            await Task.Delay(300);
            v2 = db.SchemaVersion;
            Assert.True(v2 >= 8);
        }

        // Third open: should not run any migrations
        using (var db = new DatabaseService())
        {
            await Task.Delay(100); // Give migration 9 a chance to complete if it's still running
            Assert.Equal(v2, db.SchemaVersion);
        }
    }

    [Fact]
    public void Playlist_cover_and_creation_date_survive_a_round_trip()
    {
        var cover = Path.Combine(NullWavePaths.ArtCacheDir, "cover-test.jpg");
        // Ensure directory exists for the test file path construction
        Directory.CreateDirectory(NullWavePaths.ArtCacheDir); 
        
        var created = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        using var db = new DatabaseService();
        
        // Save a playlist with a custom art path inside the data dir
        db.SavePlaylist(new Playlist 
        { 
            Name = "Test Playlist", 
            DateCreated = created, 
            CustomArtPath = cover 
        });

        // Verify raw DB storage uses the <NW_DATA> token
        using (var raw = new SQLiteConnection(_dbPath, SQLiteOpenFlags.ReadOnly))
        {
            var record = raw.Table<PlaylistRecord>().Single();
            Assert.NotNull(record.CustomArtPath);
            Assert.StartsWith(PathHelper.DataToken, record.CustomArtPath);
        }

        // Load back via service and verify resolution + date persistence
        var loaded = db.LoadPlaylists(new()).Single();
        
        Assert.Equal(Path.GetFullPath(cover), loaded.CustomArtPath);
        Assert.Equal(created, loaded.DateCreated);
    }
}