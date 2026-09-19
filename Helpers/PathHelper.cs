using System;
using System.IO;

namespace NullWave.Helpers;

public static class PathHelper
{
    public const string DataToken = "<NW_DATA>";

    /// <summary>
    /// Converts an absolute path to a portable token if it resides inside the NullWave data directory.
    /// </summary>
    public static string? Tokenize(string? absolutePath)
    {
        if (string.IsNullOrWhiteSpace(absolutePath)) return absolutePath;

        // Check current DataDir
        string dataDir = NullWavePaths.DataDir;
        if (absolutePath.StartsWith(dataDir, StringComparison.OrdinalIgnoreCase))
        {
            string relative = absolutePath.Substring(dataDir.Length);
            relative = relative.Replace('\\', '/').TrimStart('/'); // Normalize to forward slashes for DB
            return $"{DataToken}/{relative}";
        }

        // Check LegacyDataDir (just in case)
        string legacyDir = NullWavePaths.LegacyDataDir;
        if (!string.Equals(dataDir, legacyDir, StringComparison.OrdinalIgnoreCase) &&
            absolutePath.StartsWith(legacyDir, StringComparison.OrdinalIgnoreCase))
        {
            string relative = absolutePath.Substring(legacyDir.Length);
            relative = relative.Replace('\\', '/').TrimStart('/');
            return $"{DataToken}/{relative}";
        }

        // If it's outside NullWave data dir (e.g. user picked a custom music folder), leave it absolute.
        return absolutePath;
    }

    /// <summary>
    /// Resolves a portable token back to an absolute path for the current OS.
    /// </summary>
    public static string? Resolve(string? storedPath)
    {
        if (string.IsNullOrWhiteSpace(storedPath)) return storedPath;

        if (storedPath.StartsWith(DataToken, StringComparison.OrdinalIgnoreCase))
        {
            string relative = storedPath.Substring(DataToken.Length).TrimStart('/');
            relative = relative.Replace('/', Path.DirectorySeparatorChar); // Convert to OS-specific separator
            return Path.Combine(NullWavePaths.DataDir, relative);
        }

        return storedPath;
    }
}