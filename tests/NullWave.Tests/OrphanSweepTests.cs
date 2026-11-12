using System;
using System.IO;
using System.Threading.Tasks;
using NullWave.Helpers;
using NullWave.Models;
using NullWave.Services;
using Xunit;

namespace NullWave.Tests;

[Collection("Database")]
public sealed class OrphanSweepTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "nw-orphan-sweep-" + Guid.NewGuid().ToString("N"));
    private readonly DatabaseService _db;
    private readonly LibraryService _library;

    public OrphanSweepTests()
    {
        Directory.CreateDirectory(_root);
        _db = new DatabaseService();
        foreach (var track in _db.LoadAll())
            _db.Delete(track.Id);
        _library = new LibraryService(_db, null, null, startBackgroundWork: false);
    }

    public void Dispose()
    {
        _library.Dispose();
        foreach (var track in _db.LoadAll())
            _db.Delete(track.Id);
        _db.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public async Task Guard_refuses_empty_library_without_moving_files_or_asking_confirmation()
    {
        var orphan = Path.Combine(_root, "orphan.mp3");
        File.WriteAllText(orphan, "orphan");
        var askedForConfirmation = false;

        var result = await OrphanSweepCoordinator.SweepAsync(
            _library, _root, _root, (_, _) =>
            {
                askedForConfirmation = true;
                return Task.FromResult(true);
            });

        Assert.True(result.GuardRefused);
        Assert.Equal(0, result.Moved);
        Assert.False(askedForConfirmation);
        Assert.True(File.Exists(orphan));
    }

    [Fact]
    public async Task Confirmed_sweep_moves_only_orphans_and_preserves_collision_destination()
    {
        var known = Path.Combine(_root, "known.mp3");
        var orphan = Path.Combine(_root, "orphan.mp3");
        var orphanDirectory = Path.Combine(_root, "_orphans");
        var collision = Path.Combine(orphanDirectory, "orphan.mp3");
        Directory.CreateDirectory(orphanDirectory);
        File.WriteAllText(known, "known");
        File.WriteAllText(orphan, "orphan");
        File.WriteAllText(collision, "existing");
        _library.Add(new Track { Title = "Known", Artist = "Artist", FilePath = known, Source = TrackSource.Local });

        var result = await OrphanSweepCoordinator.SweepAsync(
            _library, _root, _root, (_, _) => Task.FromResult(true));

        Assert.False(result.GuardRefused);
        Assert.False(result.Cancelled);
        Assert.Equal(1, result.Moved);
        Assert.True(File.Exists(known));
        Assert.False(File.Exists(orphan));
        Assert.Equal("existing", File.ReadAllText(collision));
        Assert.Equal(2, Directory.GetFiles(orphanDirectory).Length);
        var movedFile = Assert.Single(Array.FindAll(Directory.GetFiles(orphanDirectory), path => path != collision));
        Assert.Equal("orphan", File.ReadAllText(movedFile));
        Assert.Equal("orphan_1.mp3", Path.GetFileName(movedFile));
    }

    [Fact]
    public void Dry_run_changes_nothing_and_known_files_are_not_swept()
    {
        var known = Path.Combine(_root, "known.mp3");
        var orphan = Path.Combine(_root, "orphan.mp3");
        File.WriteAllText(known, "known");
        File.WriteAllText(orphan, "orphan");
        _library.Add(new Track { Title = "Known", Artist = "Artist", FilePath = known, Source = TrackSource.Local });

        var result = _library.SweepOrphanedFiles(_root, dryRun: true);

        Assert.Equal(2, result.Scanned);
        Assert.Equal(1, result.Orphaned);
        Assert.Equal(0, result.Moved);
        Assert.True(File.Exists(known));
        Assert.True(File.Exists(orphan));
        Assert.False(Directory.Exists(Path.Combine(_root, "_orphans")));
    }

    [Fact]
    public async Task Missing_configured_directory_uses_default_and_reports_fallback()
    {
        var configured = Path.Combine(_root, "missing");
        var fallback = Path.Combine(_root, "default");
        Directory.CreateDirectory(fallback);
        var known = Path.Combine(fallback, "known.mp3");
        File.WriteAllText(known, "known");
        File.WriteAllText(Path.Combine(fallback, "orphan.mp3"), "orphan");
        _library.Add(new Track { Title = "Known", Artist = "Artist", FilePath = known, Source = TrackSource.Local });
        var result = await OrphanSweepCoordinator.SweepAsync(
            _library,
            configured,
            fallback,
            (_, _) => Task.FromResult(false));

        Assert.True(result.UsedFallback);
        Assert.True(Directory.Exists(fallback));
        Assert.True(result.Cancelled);
    }
}
