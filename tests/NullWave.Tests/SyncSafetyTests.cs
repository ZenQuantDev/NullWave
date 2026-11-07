using System;
using System.IO;
using NullWave.Helpers;
using NullWave.Models;
using NullWave.Services;
using Xunit;

namespace NullWave.Tests;

[Collection("Database")]
public sealed class SyncSafetyTests : IDisposable
{
    private readonly string _testDir;
    private readonly LibraryService _lib;
    private readonly DatabaseService _db;

    public SyncSafetyTests()
    {
        if (!NullWavePaths.DataDir.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing to run: data folder is not a temp folder.");
            
        _testDir = Path.Combine(Path.GetTempPath(), "nw-sync-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
        _db = new DatabaseService();
        _lib = new LibraryService(_db, null, null, startBackgroundWork: false);
    }

    public void Dispose() 
    { 
        _lib.Dispose(); 
        _db.Dispose();
        try { Directory.Delete(_testDir, true); } catch { }
    }

    [Fact]
    public void Imported_file_outside_downloads_is_never_renamed_or_retagged()
    {
        var file = Path.Combine(_testDir, "track01.mp3");
        File.WriteAllBytes(file, new byte[] { 1, 2, 3 });

        var track = new Track { Title = "Real Title", Artist = "Real Artist", FilePath = file, Source = TrackSource.Local };
        _lib.Add(track);

        var report = _lib.SyncLocalFilesWithLibrary(dryRun: false, currentlyPlayingPath: null);

        Assert.Equal(0, report.Renamed);
        Assert.Equal(0, report.Retagged);
        Assert.True(File.Exists(file)); // Original file untouched
    }
}