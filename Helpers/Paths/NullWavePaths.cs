using System;
using System.IO;
using System.Runtime.InteropServices;

namespace NullWave.Helpers;

public static class NullWavePaths
{
    public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    public static bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
    public static bool IsMacOS => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    // Windows: %APPDATA%\NullWave (e.g., C:\Users\Alex\AppData\Roaming\NullWave)
    // Linux/Mac: ~/.nullwave
    public static string DataDir => IsWindows 
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NullWave")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nullwave");

    public static string LogsDir      => Path.Combine(DataDir, "logs");
    public static string DownloadsDir => Path.Combine(DataDir, "downloads");
    public static string ArtCacheDir  => Path.Combine(DataDir, "art");
    public static string DatabasePath => Path.Combine(DataDir, "library.db");
    public static string KeyStorePath => Path.Combine(DataDir, "keys.enc");
    public static string ProfilePath  => Path.Combine(DataDir, "profile.json");
    public static string AvatarPath   => Path.Combine(DataDir, "avatar.png");

    private static string Home =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(LogsDir);
        Directory.CreateDirectory(DownloadsDir);
        Directory.CreateDirectory(ArtCacheDir);
    }
}