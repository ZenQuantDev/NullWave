using System;
using System.IO;
using NullWave.Helpers;
using NullWave.Services.Security;
using Serilog;

namespace NullWave.Services;

public class SecureDeleteService
{
    private readonly KeyStoreService _keyStore;
    private readonly string _nullwaveDir;

    public SecureDeleteService(KeyStoreService keyStore)
    {
        _keyStore = keyStore;
        _nullwaveDir = NullWavePaths.DataDir;
    }

    /// <summary>
    /// Refuses to proceed unless the target directory is unambiguously NullWave's own
    /// data folder (or a temp folder standing in for it during tests).
    /// </summary>
    internal static void EnsureSafeToWipe(string dir)
    {
        var full = Path.GetFullPath(dir);
        var dataDir = Path.GetFullPath(NullWavePaths.DataDir);

        var isDataDir = string.Equals(full, dataDir, StringComparison.OrdinalIgnoreCase);
        var isTempFolder = full.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase);

        if (!isDataDir && !isTempFolder)
            throw new InvalidOperationException($"Refusing to wipe '{full}': not NullWave's data folder.");
    }

    public void DeleteApiKeys() => _keyStore.DeleteAllKeys();

    public void DeleteLogs()
    {
        var logDir = NullWavePaths.LogsDir;
        if (!Directory.Exists(logDir)) return;
        EnsureSafeToWipe(_nullwaveDir);

        foreach (var file in Directory.GetFiles(logDir, "*.log"))
            DeleteFile(file);

        Log.Warning("All logs have been deleted");
    }

    public void DeleteEverything()
    {
        EnsureSafeToWipe(_nullwaveDir);

        DeleteApiKeys();
        DeleteLogs();

        if (!Directory.Exists(_nullwaveDir)) return;

        foreach (var file in Directory.EnumerateFiles(_nullwaveDir, "*", SearchOption.AllDirectories))
            DeleteFile(file);

        foreach (var dir in Directory.EnumerateDirectories(_nullwaveDir))
        {
            try { Directory.Delete(dir, recursive: true); }
            catch (Exception ex) { Log.Warning(ex, "[SecureDeleteService] Could not remove directory: {Dir}", dir); }
        }

        Log.Warning("NullWave data fully wiped");
    }

    private static void DeleteFile(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) { Log.Error(ex, "Delete failed for {Path}", path); }
    }
}