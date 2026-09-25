using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using NullWave.Helpers;
using Serilog;

namespace NullWave.Services;

public class DependencyInfo
{
    public string Name { get; init; } = string.Empty;
    public string InstalledVersion { get; init; } = string.Empty;
    public string LatestVersion { get; init; } = string.Empty;
    public bool CanSelfUpdate { get; init; }
    public bool IsInstalled { get; init; }
}

public class DependencyUpdateService
{
    private readonly HttpClient _http;

    public DependencyUpdateService()
    {
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Add("User-Agent", "NullWave-DepChecker");
    }

    // ===== YT-DLP =====
    public async Task<DependencyInfo> GetYtDlpInfoAsync()
    {
        var installed = await RunCommandAsync("yt-dlp", "--version");
        if (string.IsNullOrWhiteSpace(installed))
            return new DependencyInfo { Name = "yt-dlp", IsInstalled = false };

        string latest = "unknown";
        try
        {
            var json = await _http.GetStringAsync("https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest");
            using var doc = JsonDocument.Parse(json);
            latest = doc.RootElement.GetProperty("tag_name").GetString() ?? "unknown";
        }
        catch { }

        return new DependencyInfo
        {
            Name = "yt-dlp",
            InstalledVersion = installed.Trim(),
            LatestVersion = latest,
            CanSelfUpdate = true,
            IsInstalled = true
        };
    }

    public async Task<string> UpdateYtDlpAsync()
    {
        // 1. ALWAYS try yt-dlp's native self-updater first.
        // This works for standalone .exe, pip, and most package managers.
        var selfUpdate = await RunCommandAsync("yt-dlp", "-U");
        if (selfUpdate != null)
        {
            Log.Information("[DependencyUpdate] yt-dlp updated successfully via native self-updater (-U)");
            return "Update completed via yt-dlp";
        }

        // 2. Fallback for Windows: try winget (only works if originally installed via winget)
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Log.Information("[DependencyUpdate] Native update failed, attempting winget fallback...");
            var wingetUpdate = await RunCommandAsync("winget", "upgrade yt-dlp.yt-dlp --accept-source-agreements --accept-package-agreements");
            if (wingetUpdate != null)
            {
                Log.Information("[DependencyUpdate] yt-dlp updated successfully via winget");
                return "Update completed via winget";
            }
        }

        // 3. Fallback for Linux: try pip
        var pipUpdate = await RunCommandAsync("pip", "install --upgrade yt-dlp");
        if (pipUpdate != null)
        {
            Log.Information("[DependencyUpdate] yt-dlp updated successfully via pip");
            return "Update completed via pip";
        }

        return "Update failed: Ensure yt-dlp is installed correctly.";
    }

    // ===== VLC MEDIA PLAYER =====
    public async Task<DependencyInfo> GetVlcInfoAsync()
    {
        // 1. Try CLI (works on Linux/macOS or if added to Windows PATH)
        var installed = await RunCommandAsync("vlc", "--version");
        if (!string.IsNullOrWhiteSpace(installed))
        {
            var firstLine = installed.Split('\n')[0].Trim();
            return new DependencyInfo
            {
                Name = "VLC",
                InstalledVersion = firstLine,
                LatestVersion = "Check videolan.org",
                CanSelfUpdate = false,
                IsInstalled = true
            };
        }

        // 2. Fallback: Check standard Windows installation paths via FileVersionInfo
        if (OperatingSystem.IsWindows())
        {
            string[] standardPaths = {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "VideoLAN", "VLC", "vlc.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "VideoLAN", "VLC", "vlc.exe")
            };

            foreach (var path in standardPaths)
            {
                if (File.Exists(path))
                {
                    var versionInfo = FileVersionInfo.GetVersionInfo(path);
                    return new DependencyInfo
                    {
                        Name = "VLC",
                        InstalledVersion = versionInfo.FileVersion ?? versionInfo.ProductVersion ?? "Installed",
                        LatestVersion = "Check videolan.org",
                        CanSelfUpdate = false,
                        IsInstalled = true
                    };
                }
            }
        }

        return new DependencyInfo { Name = "VLC", IsInstalled = false };
    }

    public async Task<string> InstallVlcAsync()
    {
        if (!OperatingSystem.IsWindows())
            return "Install via your package manager (dnf/apt)";

        Log.Information("[DependencyUpdate] Attempting VLC install via winget...");
        var ok = await RunCommandAsync("winget", "install --id VideoLAN.VLC -e --accept-source-agreements --accept-package-agreements");
        if (ok != null)
        {
            Log.Information("[DependencyUpdate] VLC installed via winget");
            return "VLC installed via winget";
        }
        return "winget install failed - install VLC manually";
    }

    // ===== FFMPEG & .NET =====
    public async Task<DependencyInfo> GetFfmpegInfoAsync()
    {
        var installed = await RunCommandAsync("ffmpeg", "-version");
        if (string.IsNullOrWhiteSpace(installed))
            return new DependencyInfo { Name = "FFmpeg", IsInstalled = false };

        var firstLine = installed.Split('\n')[0].Trim();
        return new DependencyInfo
        {
            Name = "FFmpeg",
            InstalledVersion = firstLine,
            LatestVersion = "Check ffmpeg.org",
            CanSelfUpdate = false,
            IsInstalled = true
        };
    }

    public async Task<DependencyInfo> GetDotNetInfoAsync()
    {
        var installed = await RunCommandAsync("dotnet", "--version");
        return new DependencyInfo
        {
            Name = ".NET",
            InstalledVersion = installed?.Trim() ?? "unknown",
            LatestVersion = "Check dot.net",
            CanSelfUpdate = false,
            IsInstalled = !string.IsNullOrWhiteSpace(installed)
        };
    }

    // ===== HELPERS =====
    private static async Task<string?> RunCommandAsync(string cmd, string args)
    {
        try
        {
            // FIX (P9): Route through ProcessRunner to prevent stderr deadlocks 
            // and enforce a 2-minute timeout for hung package managers.
            var result = await ProcessRunner.RunAsync(cmd, args, timeout: TimeSpan.FromMinutes(2));

            if (result.ExitCode == 0) 
                return result.StandardOutput;

            // Special case: yt-dlp -U might exit non-zero in some environments but still report "up to date"
            if (cmd == "yt-dlp" && args == "-U")
            {
                var combined = result.StandardOutput + result.StandardError;
                if (combined.Contains("up to date", StringComparison.OrdinalIgnoreCase))
                    return combined;
            }

            if (!string.IsNullOrWhiteSpace(result.StandardError))
                Log.Warning("[DependencyUpdate] {Cmd} exited {Code}: {Err}", cmd, result.ExitCode, result.StandardError.Trim());

            return null;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[DependencyUpdate] Command failed: {Cmd} {Args}", cmd, args);
            return null;
        }
    }
}