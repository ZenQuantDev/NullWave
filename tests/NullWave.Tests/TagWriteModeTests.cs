using System;
using System.IO;
using NullWave.Models;
using NullWave.Services;
using Xunit;

namespace NullWave.Tests;

public class TagWriteModeTests : IDisposable
{
    private readonly string _root;
    private readonly string _downloads;
    private readonly string _music;
    private readonly string _sibling;

    public TagWriteModeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nw-tagpolicy-" + Guid.NewGuid().ToString("N"));
        _downloads = Path.Combine(_root, "downloads");
        _music = Path.Combine(_root, "music");
        _sibling = Path.Combine(_root, "downloadsOther");
        Directory.CreateDirectory(_downloads);
        Directory.CreateDirectory(_music);
        Directory.CreateDirectory(_sibling);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private static string FileIn(string directory, string name)
    {
        var path = Path.Combine(directory, name);
        File.WriteAllText(path, "x");
        return path;
    }

    [Fact]
    public void Default_is_DownloadsOnly()
        => Assert.Equal(TagWriteMode.DownloadsOnly, new Preferences().TagWriteMode);

    [Fact]
    public void Never_refuses_managed_download()
        => Assert.False(TagWritePolicy.CanWrite(
            FileIn(_downloads, "a.mp3"), TagWriteMode.Never, _downloads, null));

    [Fact]
    public void Never_refuses_external_file()
        => Assert.False(TagWritePolicy.CanWrite(
            FileIn(_music, "a.mp3"), TagWriteMode.Never, _downloads, null));

    [Fact]
    public void DownloadsOnly_allows_default_downloads_folder()
        => Assert.True(TagWritePolicy.CanWrite(
            FileIn(_downloads, "a.mp3"), TagWriteMode.DownloadsOnly, _downloads, null));

    [Fact]
    public void DownloadsOnly_allows_user_folder_when_recovery_marker_present()
    {
        var path = FileIn(_music, "Song [nw-abc123].mp3");
        Assert.True(TagWritePolicy.CanWrite(
            path, TagWriteMode.DownloadsOnly, _downloads, _music));
    }

    [Fact]
    public void DownloadsOnly_refuses_user_folder_without_recovery_marker()
    {
        var path = FileIn(_music, "Song.mp3");
        Assert.False(TagWritePolicy.CanWrite(
            path, TagWriteMode.DownloadsOnly, _downloads, _music));
    }

    [Fact]
    public void DownloadsOnly_refuses_file_outside_both_folders()
    {
        var path = FileIn(_music, "Song [nw-abc123].mp3");
        Assert.False(TagWritePolicy.CanWrite(
            path, TagWriteMode.DownloadsOnly, _downloads, _downloads));
    }

    /// <summary>
    /// Regression: when DownloadDirectory equals DownloadsDir, every unrelated path
    /// used to become managed.
    /// </summary>
    [Fact]
    public void DownloadsOnly_refuses_external_file_when_user_dir_equals_default_dir()
    {
        var path = FileIn(_music, "Song.mp3");
        Assert.False(TagWritePolicy.CanWrite(
            path, TagWriteMode.DownloadsOnly, _downloads, _downloads));
    }

    [Fact]
    public void DownloadsOnly_does_not_match_sibling_directory_prefix()
    {
        var path = FileIn(_sibling, "a.mp3");
        Assert.False(TagWritePolicy.CanWrite(
            path, TagWriteMode.DownloadsOnly, _downloads, null));
    }

    [Fact]
    public void Recovery_marker_match_is_case_insensitive()
    {
        var path = FileIn(_music, "Song [NW-ABC123].mp3");
        Assert.True(TagWritePolicy.CanWrite(
            path, TagWriteMode.DownloadsOnly, _downloads, _music));
    }

    [Fact]
    public void Always_allows_existing_external_file()
        => Assert.True(TagWritePolicy.CanWrite(
            FileIn(_music, "a.mp3"), TagWriteMode.Always, _downloads, null));

    [Fact]
    public void Always_refuses_missing_file()
        => Assert.False(TagWritePolicy.CanWrite(
            Path.Combine(_music, "ghost.mp3"), TagWriteMode.Always, _downloads, null));

    [Theory]
    [InlineData(TagWriteMode.Never)]
    [InlineData(TagWriteMode.DownloadsOnly)]
    [InlineData(TagWriteMode.Always)]
    public void Null_and_empty_paths_are_always_refused(TagWriteMode mode)
    {
        Assert.False(TagWritePolicy.CanWrite(null, mode, _downloads, _music));
        Assert.False(TagWritePolicy.CanWrite(string.Empty, mode, _downloads, _music));
    }
}
