using System;
using System.IO;

namespace NullWave.Helpers;

public static class PathHelper
{
    public const string DataToken = "<NW_DATA>";

    private static bool IsInside(string path, string dir)
    {
        if (!path.StartsWith(dir, StringComparison.OrdinalIgnoreCase)) return false;
        if (path.Length == dir.Length) return true;
        var nextChar = path[dir.Length];
        return nextChar == Path.DirectorySeparatorChar || nextChar == Path.AltDirectorySeparatorChar;
    }

    public static string? Tokenize(string? absolutePath)
    {
        if (string.IsNullOrWhiteSpace(absolutePath)) return absolutePath;

        string dataDir = NullWavePaths.DataDir;
        if (IsInside(absolutePath, dataDir))
        {
            string relative = absolutePath.Substring(dataDir.Length);
            relative = relative.Replace('\\', '/').TrimStart('/');
            return $"{DataToken}/{relative}";
        }

        string legacyDir = NullWavePaths.LegacyDataDir;
        if (!string.Equals(dataDir, legacyDir, StringComparison.OrdinalIgnoreCase) &&
            IsInside(absolutePath, legacyDir))
        {
            string relative = absolutePath.Substring(legacyDir.Length);
            relative = relative.Replace('\\', '/').TrimStart('/');
            return $"{DataToken}/{relative}";
        }

        return absolutePath;
    }

    public static string? Resolve(string? storedPath)
    {
        if (string.IsNullOrWhiteSpace(storedPath)) return storedPath;

        if (storedPath.StartsWith(DataToken, StringComparison.OrdinalIgnoreCase))
        {
            string relative = storedPath.Substring(DataToken.Length).TrimStart('/');
            relative = relative.Replace('/', Path.DirectorySeparatorChar);
            return Path.Combine(NullWavePaths.DataDir, relative);
        }

        return storedPath;
    }
}