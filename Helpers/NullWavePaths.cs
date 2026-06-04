using System;
using System.IO;

namespace NullWave.Helpers;

/// <summary>
/// Single source of truth for every file path NullWave uses.
/// Import this anywhere instead of scattering Path.Combine() calls.
/// </summary>
public static class NullWavePaths
{
    public static string DataDir     => Path.Combine(Home, ".nullwave");
    public static string LogsDir     => Path.Combine(DataDir, "logs");
    public static string DownloadsDir => Path.Combine(DataDir, "downloads");
    public static string ArtCacheDir => Path.Combine(DataDir, "art");
    public static string DatabasePath => Path.Combine(DataDir, "library.db");
    public static string KeyStorePath => Path.Combine(DataDir, "keys.enc");
    public static string ProfilePath  => Path.Combine(DataDir, "profile.json");
    public static string AvatarPath   => Path.Combine(DataDir, "avatar.png");

    private static string Home =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>Ensure all required directories exist. Call once at startup.</summary>
    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(LogsDir);
        Directory.CreateDirectory(DownloadsDir);
        Directory.CreateDirectory(ArtCacheDir);
    }
}