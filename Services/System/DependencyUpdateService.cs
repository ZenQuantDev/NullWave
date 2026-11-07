using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

public sealed record PackageInstallExecution(bool Approved, ProcessResult? ProcessResult);

public class DependencyUpdateService
{
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan ActionTimeout = TimeSpan.FromMinutes(2);

    // 0x8A15002B (APPINSTALLER_CLI_ERROR_UPDATE_NOT_APPLICABLE) as a signed int.
    // This is what winget returns when it manages a package but there is no newer version.
    private const int WingetNoUpdateExitCode = unchecked((int)0x8A15002B);

    private readonly HttpClient _http;
    private readonly Func<string, IList<string>, TimeSpan, Task<ProcessResult>> _packageProcessRunner;

    public const string InstallCancelledStatus = "Install cancelled by user";

    public DependencyUpdateService(
        Func<string, IList<string>, TimeSpan, Task<ProcessResult>>? packageProcessRunner = null)
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        _http.DefaultRequestHeaders.Add("User-Agent", "NullWave-DepChecker");
        _packageProcessRunner = packageProcessRunner
            ?? ((executable, arguments, timeout) => ProcessRunner.RunAsync(executable, arguments, timeout: timeout));
    }

    // ===== YT-DLP =====
    public async Task<DependencyInfo> GetYtDlpInfoAsync()
    {
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
            ToolVersionProbe.Invalidate("yt-dlp");
        }
    }

    private static async Task<string> UpdateYtDlpCoreAsync()
    {
        // 1. ALWAYS try yt-dlp's native self-updater first.
        var selfUpdate = await RunCommandAsync("yt-dlp", "-U");
        if (selfUpdate != null)
        {
            if (selfUpdate.Contains("up to date", StringComparison.OrdinalIgnoreCase))
            {
                Log.Information("[DependencyUpdate] yt-dlp is already up to date");
                return "Already up to date";
            }
            Log.Information("[DependencyUpdate] yt-dlp updated successfully via native self-updater (-U)");
            return "Update completed via yt-dlp";
        }

        // 2. Fallbacks disabled: silent winget/pip updates bypass the S7 consent gate.
        // Users must update yt-dlp manually or via their OS package manager.
        return "Update failed: Ensure yt-dlp is installed correctly.";
    }

    // ===== VLC MEDIA PLAYER =====
    public async Task<DependencyInfo> GetVlcInfoAsync()
    {
        if (OperatingSystem.IsWindows())
        {
            var found = FindWindowsVlcVersion();
            if (found != null)
            {
                return new DependencyInfo
                {
                    Name = "VLC",
                    InstalledVersion = found,
                    LatestVersion = "Check videolan.org",
                    CanSelfUpdate = false,
                    IsInstalled = true
                };
            }

            var resolution = VlcLocator.Current;
            if (resolution.Chosen != null)
            {
                return new DependencyInfo
                {
                    Name = "VLC",
                    InstalledVersion = $"bundled ({resolution.Chosen.Source})",
                    LatestVersion = "Ships with NullWave",
                    CanSelfUpdate = false,
                    IsInstalled = true
                };
            }

            return new DependencyInfo { Name = "VLC", IsInstalled = false };
        }

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

    public async Task<string> InstallVlcAsync(Func<string, Task<bool>> requestConsent)
    {
        if (!OperatingSystem.IsWindows())
            return "Install via your package manager (dnf/apt)";

        var result = await RunPackageInstallAsync(
            "winget",
            new List<string>
            {
                "install", "--id", "VideoLAN.VLC", "-e",
                "--accept-source-agreements", "--accept-package-agreements"
            },
            requestConsent);
        if (!result.Approved)
            return InstallCancelledStatus;

        var process = result.ProcessResult
            ?? throw new InvalidOperationException("An approved package install must have a process result.");
        if (process.ExitCode == 0 && !process.TimedOut && !process.Canceled)
        {
            Log.Information("[DependencyUpdate] VLC installed via winget");
            VlcLocator.Invalidate();
            return "VLC installed via winget";
        }

        Log.Warning(
            "[DependencyUpdate] winget install failed with exit code {ExitCode}; timed out: {TimedOut}; canceled: {Canceled}",
            process.ExitCode, process.TimedOut, process.Canceled);
        return "winget install failed - install VLC manually";
    }

    public async Task<PackageInstallExecution> RunPackageInstallAsync(
        string executable,
        IList<string> arguments,
        Func<string, Task<bool>> requestConsent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(requestConsent);

        var command = FormatCommand(executable, arguments);
        if (!await requestConsent(command))
            return new PackageInstallExecution(false, null);

        Log.Information("[DependencyUpdate] Running approved package install: {Command}", command);
        var result = await _packageProcessRunner(executable, arguments, ActionTimeout);
        return new PackageInstallExecution(true, result);
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

    private static Task<string?> RunQueryAsync(string cmd, params string[] args)
        => RunWithTimeoutAsync(QueryTimeout, cmd, args);

    private static Task<string?> RunCommandAsync(string cmd, params string[] args)
        => RunWithTimeoutAsync(ActionTimeout, cmd, args);

    private static string FormatCommand(string executable, IList<string> arguments)
    {
        static string FormatArgument(string argument)
        {
            if (argument.Length > 0 && argument.IndexOfAny(new[] { ' ', '\t', '"' }) < 0)
                return argument;
            return "\"" + argument.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
        }

        return string.Join(" ", new[] { executable }.Concat(arguments).Select(FormatArgument));
    }

    private static async Task<string?> RunWithTimeoutAsync(TimeSpan timeout, string cmd, string[] args)
    {
        try
        {
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
            
            // winget exits with UPDATE_NOT_APPLICABLE when nothing newer exists: that's success.
            // Any other non-zero code (like "no installed package found") is a real failure.
            if (cmd == "winget" && args.Length > 0 && args[0] == "upgrade" 
                && result.ExitCode == WingetNoUpdateExitCode)
                return result.StandardOutput;

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