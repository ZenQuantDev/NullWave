using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using NullWave.Helpers;
using NullWave.Models;
using NullWave.Services;
using Xunit;

namespace NullWave.Tests;

[Collection("Database")]
public sealed class LibraryFileSafetyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "nw-library-safety-" + Guid.NewGuid().ToString("N"));
    private readonly DatabaseService _db;
    private readonly PreferencesService _preferences;
    private readonly LibraryService _library;

    public LibraryFileSafetyTests()
    {
        Directory.CreateDirectory(_root);
        _db = new DatabaseService();
        foreach (var track in _db.LoadAll())
            _db.Delete(track.Id);
        _preferences = new PreferencesService(Path.Combine(_root, "prefs.json"));
        _library = new LibraryService(_db, null, _preferences, startBackgroundWork: false);
    }

    public void Dispose()
    {
        _library.Dispose();
        foreach (var track in _db.LoadAll())
            _db.Delete(track.Id);
        _preferences.Dispose();
        _db.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Theory]
    [InlineData(TagWriteMode.Never, true, false)]
    [InlineData(TagWriteMode.Never, false, false)]
    [InlineData(TagWriteMode.DownloadsOnly, true, true)]
    [InlineData(TagWriteMode.DownloadsOnly, false, false)]
    [InlineData(TagWriteMode.Always, true, true)]
    [InlineData(TagWriteMode.Always, false, true)]
    public void CanWriteTags_obeys_mode_and_configured_directory_marker(
        TagWriteMode mode, bool hasManagedMarker, bool expected)
    {
        var configuredDirectory = Path.Combine(_root, "configured-downloads");
        Directory.CreateDirectory(configuredDirectory);
        var fileName = hasManagedMarker ? "track [nw-12345678].mp3" : "track.mp3";
        var filePath = Path.Combine(configuredDirectory, fileName);
        File.WriteAllBytes(filePath, new byte[] { 1, 2, 3 });
        _preferences.Update(p =>
        {
            p.DownloadDirectory = configuredDirectory;
            p.TagWriteMode = mode;
        });

        Assert.Equal(expected, _library.CanWriteTags(filePath));
    }

    [Fact]
    public void TryRelink_finds_FileNamer_name_with_new_nw_id_suffix()
    {
        var downloads = Path.Combine(_root, "downloads");
        Directory.CreateDirectory(downloads);
        _preferences.Update(p => p.DownloadDirectory = downloads);
        var track = new Track
        {
            Title = "A renamed title",
            Artist = "An artist",
            FilePath = Path.Combine(_root, "missing.mp3"),
            Source = TrackSource.Local
        };
        var fileName = FileNamer.Generate(track.Artist, track.Title, track.Id.ToString(), ".mp3");
        var renamedPath = Path.Combine(downloads, fileName);
        File.WriteAllBytes(renamedPath, new byte[] { 1, 2, 3 });

        Assert.True(_library.TryRelink(track));
        Assert.Equal(renamedPath, track.FilePath);
    }

    [Fact]
    public void Removing_unmanaged_track_deletes_only_the_library_row()
    {
        var configuredDownloads = Path.Combine(_root, "configured-downloads");
        var importedDirectory = Path.Combine(_root, "imported");
        Directory.CreateDirectory(configuredDownloads);
        Directory.CreateDirectory(importedDirectory);
        _preferences.Update(p => p.DownloadDirectory = configuredDownloads);
        var filePath = Path.Combine(importedDirectory, "personal-track.mp3");
        File.WriteAllBytes(filePath, new byte[] { 1, 2, 3, 4 });
        var track = new Track
        {
            Title = "Imported",
            Artist = "User",
            FilePath = filePath,
            Source = TrackSource.Local
        };
        _library.Add(track);

        _library.Remove(track.Id, recycleFile: true);

        Assert.True(File.Exists(filePath));
        Assert.DoesNotContain(_library.GetAll(), item => item.Id == track.Id);
        Assert.DoesNotContain(_db.LoadAll(), item => item.Id == track.Id);
    }

    [Fact]
    public async Task Folder_export_keeps_original_file_bytes_unchanged()
    {
        var sourceDirectory = Path.Combine(_root, "source");
        var exportDirectory = Path.Combine(_root, "export");
        Directory.CreateDirectory(sourceDirectory);
        var sourcePath = Path.Combine(sourceDirectory, "original.mp3");
        var originalBytes = new byte[] { 0, 1, 2, 3, 4, 5, 250, 255 };
        await File.WriteAllBytesAsync(sourcePath, originalBytes);
        var originalHash = SHA256.HashData(await File.ReadAllBytesAsync(sourcePath));
        var track = new Track
        {
            Title = "Exported",
            Artist = "Artist",
            FilePath = sourcePath,
            Source = TrackSource.Local
        };

        var result = await new ExportService().ExportToFolderAsync(new[] { track }, exportDirectory);

        Assert.Equal(1, result.Copied);
        Assert.Equal(originalHash, SHA256.HashData(await File.ReadAllBytesAsync(sourcePath)));
    }
}
