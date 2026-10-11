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

    /// <summary>
    /// Turns a stored path back into a real one. A path stored with the data token must stay inside
    /// the data folder: "&lt;NW_DATA&gt;/../../somewhere" (from a damaged or hand-edited database or a
    /// shared profile) returns null instead of pointing at a file elsewhere on the disk.
    /// Paths without the token are returned unchanged.
    /// </summary>
    public static string? Resolve(string? storedPath)
    {
        if (string.IsNullOrWhiteSpace(storedPath)) return storedPath;

        if (storedPath.StartsWith(DataToken, StringComparison.OrdinalIgnoreCase))
        {
            string relative = storedPath.Substring(DataToken.Length).TrimStart('/');
            relative = relative.Replace('/', Path.DirectorySeparatorChar);

            var dataDir = Path.GetFullPath(NullWavePaths.DataDir);
            var combined = Path.GetFullPath(Path.Combine(dataDir, relative));

            return IsInside(combined, dataDir) ? combined : null;
        }

        return storedPath;
    }
}