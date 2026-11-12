using System;
using System.IO;
using NullWave.Models;

namespace NullWave.Services;

/// <summary>
/// Pure decision logic for tag writes. LibraryService.CanWriteTags is the only
/// production caller; keeping it free of database and preferences dependencies
/// lets the safety rule be tested directly.
/// </summary>
public static class TagWritePolicy
{
    public const string RecoveryMarker = "[nw-";

    /// <summary>
    /// The default DownloadsDir is always managed. A user-configured DownloadDirectory
    /// is only managed for files whose NAME carries the "[nw-" marker NullWave writes,
    /// so pointing the pref at a personal music folder doesn't retag everything in it.
    /// </summary>
    public static bool IsManagedDownload(string? filePath, string defaultDownloadsDir, string? userDownloadDir)
    {
        if (string.IsNullOrEmpty(filePath)) return false;
        try
        {
            var fullPath = Path.GetFullPath(filePath);
            var defaultDir = Path.GetFullPath(defaultDownloadsDir);

            if (IsUnderOrEqual(fullPath, defaultDir)) return true;

            if (!string.IsNullOrWhiteSpace(userDownloadDir))
            {
                try
                {
                    var userDir = Path.GetFullPath(userDownloadDir);

                    // The default path check above already decided whether this file is managed.
                    if (string.Equals(userDir, defaultDir, StringComparison.OrdinalIgnoreCase)) return false;
                    if (!IsUnderOrEqual(fullPath, userDir)) return false;

                    return Path.GetFileName(fullPath).Contains(
                        RecoveryMarker, StringComparison.OrdinalIgnoreCase);
                }
                catch { }
            }

            return false;
        }
        catch { return false; }
    }

    public static bool CanWrite(
        string? filePath, TagWriteMode mode, string defaultDownloadsDir, string? userDownloadDir)
        => mode switch
        {
            TagWriteMode.Never => false,
            TagWriteMode.DownloadsOnly =>
                IsManagedDownload(filePath, defaultDownloadsDir, userDownloadDir),
            TagWriteMode.Always => !string.IsNullOrEmpty(filePath) && File.Exists(filePath),
            _ => false
        };

    private static bool IsUnderOrEqual(string fullPath, string directory)
    {
        var withSeparator = directory.EndsWith(
            Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? directory
            : directory + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(withSeparator, StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                fullPath,
                directory.TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
    }
}
