using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Serilog;

namespace NullWave.Services;

public class DependencyInfo
{
    public string Name          { get; init; } = string.Empty;
    public string InstalledVersion { get; init; } = string.Empty;
    public string LatestVersion { get; init; } = string.Empty;
    public bool   CanSelfUpdate { get; init; }
    public bool   IsInstalled   { get; init; }
}

public class DependencyUpdateService
{
    private readonly HttpClient _http;

    public DependencyUpdateService()
    {
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Add("User-Agent", "NullWave-DepChecker");
    }

    // ── yt-dlp ────────────────────────────────────────────────────────────

    public async Task<DependencyInfo> GetYtDlpInfoAsync()
    {
        var installed = await RunCommandAsync("yt-dlp", "--version");
        if (installed == null)
            return new DependencyInfo { Name = "yt-dlp", IsInstalled = false };

        string latest = "unknown";
        try
        {
            var json = await _http.GetStringAsync(
                "https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest");
            using var doc = JsonDocument.Parse(json);
            latest = doc.RootElement
                .GetProperty("tag_name").GetString() ?? "unknown";
        }
        catch { }

        return new DependencyInfo
        {
            Name             = "yt-dlp",
            InstalledVersion = installed.Trim(),
            LatestVersion    = latest,
            CanSelfUpdate    = true,
            IsInstalled      = true
        };
    }

    public async Task<string> UpdateYtDlpAsync()
    {
        try
        {
            var result = await RunCommandAsync("yt-dlp", "-U");
            Log.Information("[DependencyUpdate] yt-dlp update: {Result}", result);
            return result ?? "Update completed";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[DependencyUpdate] yt-dlp update failed");
            return $"Update failed: {ex.Message}";
        }
    }

    // ── VLC ───────────────────────────────────────────────────────────────

    public async Task<DependencyInfo> GetVlcInfoAsync()
    {
        var installed = await RunCommandAsync("vlc", "--version");
        if (installed == null)
            return new DependencyInfo { Name = "VLC", IsInstalled = false };

        var firstLine = installed.Split('\n')[0].Trim();
        return new DependencyInfo
        {
            Name             = "VLC",
            InstalledVersion = firstLine,
            LatestVersion    = "Check vlc.videolan.org",
            CanSelfUpdate    = false,
            IsInstalled      = true
        };
    }

    // ── FFmpeg ────────────────────────────────────────────────────────────

    public async Task<DependencyInfo> GetFfmpegInfoAsync()
    {
        var installed = await RunCommandAsync("ffmpeg", "-version");
        if (installed == null)
            return new DependencyInfo { Name = "FFmpeg", IsInstalled = false };

        var firstLine = installed.Split('\n')[0].Trim();
        return new DependencyInfo
        {
            Name             = "FFmpeg",
            InstalledVersion = firstLine,
            LatestVersion    = "Check ffmpeg.org",
            CanSelfUpdate    = false,
            IsInstalled      = true
        };
    }

    // ── .NET ──────────────────────────────────────────────────────────────

    public async Task<DependencyInfo> GetDotNetInfoAsync()
    {
        var installed = await RunCommandAsync("dotnet", "--version");
        return new DependencyInfo
        {
            Name             = ".NET",
            InstalledVersion = installed?.Trim() ?? "unknown",
            LatestVersion    = "Check dot.net",
            CanSelfUpdate    = false,
            IsInstalled      = installed != null
        };
    }

    // ── Helper ────────────────────────────────────────────────────────────

    private static async Task<string?> RunCommandAsync(string cmd, string args)
    {
        try
        {
            var psi = new ProcessStartInfo(cmd, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return null;
            var output = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();
            return string.IsNullOrWhiteSpace(output) ? null : output;
        }
        catch { return null; }
    }
}