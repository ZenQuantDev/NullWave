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
}