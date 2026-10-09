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
    // Asking a tool for its version should take a moment. Installing or updating one can take minutes.
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan ActionTimeout = TimeSpan.FromMinutes(2);

    private readonly HttpClient _http;

    public DependencyUpdateService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        _http.DefaultRequestHeaders.Add("User-Agent", "NullWave-DepChecker");
    }

    // ===== YT-DLP =====
    public async Task<DependencyInfo> GetYtDlpInfoAsync()
    {
        // Shared with the startup diagnostics and the yt-dlp plugin: one lookup per run.
        var installed = await ToolVersionProbe.GetVersionAsync("yt-dlp");
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
        try
        {
            return await UpdateYtDlpCoreAsync();
        }
        finally
        {
            // Whatever happened, the installed version may have changed: forget the cached answer.
            ToolVersionProbe.Invalidate("yt-dlp");
        }
    }

    private static async Task<string> UpdateYtDlpCoreAsync()
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
            var wingetUpdate = await RunCommandAsync("winget", "upgrade", "yt-dlp.yt-dlp", "--accept-source-agreements", "--accept-package-agreements");
            if (wingetUpdate != null)
            {
                Log.Information("[DependencyUpdate] yt-dlp updated successfully via winget");
                return "Update completed via winget";
            }
        }

        // 3. Fallback for Linux: try pip
        var pipUpdate = await RunCommandAsync("pip", "install", "--upgrade", "yt-dlp");
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
        if (OperatingSystem.IsWindows())
        {
            // NEVER run "vlc --version" on Windows: VLC opens its own console window, shows
            // "Press RETURN to continue..." and waits. Read the version from vlc.exe instead.
            var found = FindWindowsVlcVersion();
            return found != null
                ? new DependencyInfo
                {
                    Name = "VLC",
                    InstalledVersion = found,
                    LatestVersion = "Check videolan.org",
                    CanSelfUpdate = false,
                    IsInstalled = true
                }
                : new DependencyInfo { Name = "VLC", IsInstalled = false };
        }

        // Linux / macOS: the command line is safe and is how VLC is normally found.
        var installed = await ToolVersionProbe.GetVersionAsync("vlc");
        if (!string.IsNullOrWhiteSpace(installed))
        {
            return new DependencyInfo
            {
                Name = "VLC",
                InstalledVersion = installed.Trim(),
                LatestVersion = "Check videolan.org",
                CanSelfUpdate = false,
                IsInstalled = true
            };
        }

        return new DependencyInfo { Name = "VLC", IsInstalled = false };
    }

    private static string? FindWindowsVlcVersion()
    {
        var candidates = new System.Collections.Generic.List<string>();

        var resolved = PlatformHelper.ResolveVlcDirectory();
        if (resolved != null) candidates.Add(Path.Combine(resolved, "vlc.exe"));

        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "VideoLAN", "VLC", "vlc.exe"));
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "VideoLAN", "VLC", "vlc.exe"));

        foreach (var path in candidates)
        {
            try
            {
                if (!File.Exists(path)) continue;
                var info = FileVersionInfo.GetVersionInfo(path);
                return info.FileVersion ?? info.ProductVersion ?? "Installed";
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[DependencyUpdate] Could not read VLC version from {Path}", path);
            }
        }

        return null;
    }

    public async Task<string> InstallVlcAsync()
    {
        if (!OperatingSystem.IsWindows())
            return "Install via your package manager (dnf/apt)";

        Log.Information("[DependencyUpdate] Attempting VLC install via winget...");
        var ok = await RunCommandAsync("winget", "install", "--id", "VideoLAN.VLC", "-e", "--accept-source-agreements", "--accept-package-agreements");
        if (ok != null)
        {
            Log.Information("[DependencyUpdate] VLC installed via winget");
            VlcLocator.Invalidate();   // next ResolveVlcDirectory() sees the fresh install
            return "VLC installed via winget";
        }
        return "winget install failed - install VLC manually";
    }

    // ===== FFMPEG & .NET =====
    public async Task<DependencyInfo> GetFfmpegInfoAsync()
    {
        var installed = await RunQueryAsync("ffmpeg", "-version");
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

    public Task<DependencyInfo> GetDotNetInfoAsync()
    {
        // NullWave ships with its own .NET runtime, so there is nothing to install. Report the one
        // that is running instead of looking for a "dotnet" command that most users do not have.
        return Task.FromResult(new DependencyInfo
        {
            Name = ".NET",
            InstalledVersion = Environment.Version.ToString(),
            LatestVersion = "Check dot.net",
            CanSelfUpdate = false,
            IsInstalled = true
        });
    }

    // ===== HELPERS =====

    /// <summary>For "what version is this?" questions: short timeout.</summary>
    private static Task<string?> RunQueryAsync(string cmd, params string[] args)
        => RunWithTimeoutAsync(QueryTimeout, cmd, args);

    /// <summary>For installs and updates: long timeout.</summary>
    private static Task<string?> RunCommandAsync(string cmd, params string[] args)
        => RunWithTimeoutAsync(ActionTimeout, cmd, args);

    private static async Task<string?> RunWithTimeoutAsync(TimeSpan timeout, string cmd, string[] args)
    {
        try
        {
            // FIX (P9): Route through ProcessRunner to prevent stderr deadlocks
            // and enforce a timeout for hung tools and package managers.
            var result = await ProcessRunner.RunAsync(cmd, args, timeout: timeout);

            if (result.TimedOut)
            {
                Log.Warning("[DependencyUpdate] {Cmd} timed out after {Seconds:F0}s", cmd, timeout.TotalSeconds);
                return null;
            }

            if (result.ExitCode == 0)
                return result.StandardOutput;

            // Special case: yt-dlp -U might exit non-zero in some environments but still report "up to date"
            if (cmd == "yt-dlp" && args.Length == 1 && args[0] == "-U")
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
            Log.Warning(ex, "[DependencyUpdate] Command failed: {Cmd} {Args}", cmd, string.Join(" ", args));
            return null;
        }
    }
}