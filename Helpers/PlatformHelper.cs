using System;
using System.IO;
using System.Runtime.InteropServices;

namespace NullWave.Helpers;

/// <summary>
/// Cross-platform helper for resolving native executables and library paths.
/// On Windows, searches common installation directories (Program Files, WinGet, etc.)
/// when tools are not in the system PATH.
/// </summary>
public static class PlatformHelper
{
    /// <summary>
    /// Resolves the full path to an executable. On Windows, checks common install locations
    /// before falling back to just the name (which relies on PATH).
    /// </summary>
    public static string ResolveExecutable(string name)
    {
        if (NullWavePaths.IsWindows)
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            var candidates = new[]
            {
                // VLC
                Path.Combine(programFiles, "VideoLAN", "VLC", $"{name}.exe"),
                Path.Combine(programFilesX86, "VideoLAN", "VLC", $"{name}.exe"),
                
                // yt-dlp (WinGet installs to Links folder which is in PATH, but check anyway)
                Path.Combine(localAppData, "Microsoft", "WinGet", "Links", $"{name}.exe"),
                Path.Combine(localAppData, "Microsoft", "WinGet", "Packages", "yt-dlp.yt-dlp_Microsoft.Winget.Source_8wekyb3d8bbwe", $"{name}.exe"),
                Path.Combine(localAppData, "Programs", "yt-dlp", $"{name}.exe"),
                
                // FFmpeg
                Path.Combine(programFiles, "ffmpeg", "bin", $"{name}.exe"),
                Path.Combine(programFiles, "FFmpeg", "bin", $"{name}.exe"),
                Path.Combine(programFilesX86, "ffmpeg", "bin", $"{name}.exe"),
                
                // Ollama
                Path.Combine(localAppData, "Programs", "Ollama", $"{name}.exe"),
                Path.Combine(programFiles, "Ollama", $"{name}.exe")
            };

            foreach (var path in candidates)
            {
                if (File.Exists(path)) return path;
            }
            
            // Fallback: append .exe and hope it's in PATH
            return $"{name}.exe";
        }

        // Linux/Mac: just return the name, OS will search PATH
        return name;
    }

    /// <summary>
    /// Resolves the directory containing LibVLC native libraries (libvlc.dll).
    /// Required for LibVLCSharp.Core.Initialize() on Windows.
    /// Returns null on Linux/Mac (LibVLCSharp finds it automatically there).
    /// </summary>
    public static string? ResolveVlcDirectory()
    {
        if (!NullWavePaths.IsWindows) return null;

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        var candidates = new[]
        {
            Path.Combine(programFiles, "VideoLAN", "VLC"),
            Path.Combine(programFilesX86, "VideoLAN", "VLC")
        };

        foreach (var dir in candidates)
        {
            if (Directory.Exists(dir) && File.Exists(Path.Combine(dir, "libvlc.dll")))
                return dir;
        }

        return null;
    }
}