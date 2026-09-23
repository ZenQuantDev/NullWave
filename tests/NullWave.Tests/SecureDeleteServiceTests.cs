using System;
using System.IO;
using System.Linq;
using NullWave.Helpers;
using NullWave.Services;
using NullWave.Services.Security;
using Xunit;

namespace NullWave.Tests;

[Collection("Database")]
public class SecureDeleteServiceTests : IDisposable
{
    private readonly string _dataDir = NullWavePaths.DataDir;

    public SecureDeleteServiceTests()
    {
        if (!_dataDir.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing to run: data folder is not a temp folder.");

        Directory.CreateDirectory(_dataDir);
        Cleanup();
    }

    public void Dispose() => Cleanup();

    private void Cleanup()
    {
        try
        {
            foreach (var entry in Directory.GetFileSystemEntries(_dataDir))
            {
                if (Directory.Exists(entry)) Directory.Delete(entry, recursive: true);
                else File.Delete(entry);
            }
        }
        catch { /* best effort between tests */ }
    }

    private KeyStoreService MakeStore() =>
        new(Path.Combine(_dataDir, "keys.enc"), System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

    [Fact]
    public void DeleteApiKeys_removes_the_keystore_file()
    {
        var store = MakeStore();
        store.SaveKey("A", "1");
        var service = new SecureDeleteService(store);

        service.DeleteApiKeys();

        Assert.False(File.Exists(Path.Combine(_dataDir, "keys.enc")));
    }

    [Fact]
    public void DeleteLogs_removes_every_log_file_but_leaves_the_folder()
    {
        var logDir = NullWavePaths.LogsDir;
        Directory.CreateDirectory(logDir);
        File.WriteAllText(Path.Combine(logDir, "a.log"), "x");
        File.WriteAllText(Path.Combine(logDir, "b.log"), "y");
        var service = new SecureDeleteService(MakeStore());

        service.DeleteLogs();

        Assert.Empty(Directory.GetFiles(logDir, "*.log"));
        Assert.True(Directory.Exists(logDir));
    }

    [Fact]
    public void DeleteEverything_removes_files_inside_subfolders_not_just_top_level()
    {
        Directory.CreateDirectory(NullWavePaths.BackupsDir);
        Directory.CreateDirectory(NullWavePaths.DownloadsDir);
        var backupFile = Path.Combine(NullWavePaths.BackupsDir, "library-20260101-000000.db");
        var downloadFile = Path.Combine(NullWavePaths.DownloadsDir, "song.mp3");
        File.WriteAllText(backupFile, "fake backup");
        File.WriteAllText(downloadFile, "fake mp3");
        var service = new SecureDeleteService(MakeStore());

        service.DeleteEverything();

        Assert.False(File.Exists(backupFile));
        Assert.False(File.Exists(downloadFile));
    }

    [Fact]
    public void DeleteEverything_handles_large_files_without_throwing()
    {
        var bigFile = Path.Combine(_dataDir, "big.bin");
        File.WriteAllBytes(bigFile, new byte[3 * 1024 * 1024]);
        var service = new SecureDeleteService(MakeStore());

        var ex = Record.Exception(() => service.DeleteEverything());

        Assert.Null(ex);
        Assert.False(File.Exists(bigFile));
    }

    [Fact]
    public void DeleteEverything_refuses_outside_a_temp_or_data_folder()
    {
        var outsidePath = Path.Combine(Path.GetTempPath(), "..", "not-nullwave-data");
        Assert.Throws<InvalidOperationException>(() => SecureDeleteService.EnsureSafeToWipe(outsidePath));
    }
}