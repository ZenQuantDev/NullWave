using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NullWave.Helpers;
using Serilog;

namespace NullWave.Services;

/// <summary>
/// Finds out which version of an external tool (yt-dlp, vlc, ...) is installed, once per run.
///
/// Why it exists: the startup log showed yt-dlp being launched twice at startup, about 1.5 to 2 s
/// each (PyInstaller one-file builds unpack themselves on every launch). On Windows this reads the
/// version from the executable's file properties instead, so no process is started at all; other
/// platforms (or an exe without usable version info) fall back to a single "--version" call with a
/// timeout. Successful answers are cached; "not found" answers are not, so installing a tool
/// mid-session is picked up the next time someone asks.
/// </summary>
public static class ToolVersionProbe
{
    private static readonly ConcurrentDictionary<string, Lazy<Task<string?>>> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Test seam. Production code leaves this on the default resolver.</summary>
    internal static Func<string, CancellationToken, Task<string?>> Resolver { get; set; } = ResolveAsync;

    /// <summary>
    /// Forget a cached answer. Call this after installing or updating a tool, otherwise the next
    /// question still gets the old version.
    /// </summary>
    public static void Invalidate(string tool) => Cache.TryRemove(tool, out _);

    internal static void ResetForTests()
    {
        Cache.Clear();
        Resolver = ResolveAsync;
    }

    /// <summary>Returns the version text, or null if the tool is missing or could not be read.</summary>
    public static async Task<string?> GetVersionAsync(string tool)
    {
        var entry = Cache.GetOrAdd(tool,
            t => new Lazy<Task<string?>>(() => Resolver(t, CancellationToken.None)));

        string? version = null;
        try
        {
            version = await entry.Value.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[ToolVersionProbe] Probe for {Tool} failed", tool);
        }

        if (version is null)
            Cache.TryRemove(new KeyValuePair<string, Lazy<Task<string?>>>(tool, entry));

        return version;
    }

    private static async Task<string?> ResolveAsync(string tool, CancellationToken ct)
    {
        var exe = PlatformHelper.ResolveExecutable(tool);

        if (OperatingSystem.IsWindows())
        {
            var fullPath = Path.IsPathRooted(exe) && File.Exists(exe)
                ? exe
                : FindOnPath(exe, Environment.GetEnvironmentVariable("PATH"));

            if (fullPath != null)
            {
                try
                {
                    var info = FileVersionInfo.GetVersionInfo(fullPath);
                    var calVer = FormatCalVer(info.FileMajorPart, info.FileMinorPart,
                        info.FileBuildPart, info.FilePrivatePart);
                    if (calVer != null) return calVer;
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "[ToolVersionProbe] Could not read file version of {Path}", fullPath);
                }
            }
        }

        var result = await ProcessRunner.RunAsync(exe, new[] { "--version" }, TimeSpan.FromSeconds(8), ct);
        if (result.TimedOut || result.Canceled || result.ExitCode != 0) return null;

        var text = string.IsNullOrWhiteSpace(result.StandardOutput) ? result.StandardError : result.StandardOutput;
        var firstLine = text?
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        return string.IsNullOrWhiteSpace(firstLine) ? null : firstLine;
    }

    /// <summary>
    /// yt-dlp versions are dates (2026.08.19). Windows stores them as four numbers (2026, 8, 19, 0).
    /// Anything that does not look like a year in the first number is rejected so the caller can fall
    /// back to asking the tool itself.
    /// </summary>
    internal static string? FormatCalVer(int major, int minor, int build, int revision)
    {
        if (major < 2000 || major > 2999) return null;
        if (minor is < 1 or > 12 || build is < 1 or > 31) return null;

        var text = $"{major}.{minor:D2}.{build:D2}";
        return revision > 0 ? $"{text}.{revision}" : text;
    }

    /// <summary>Looks for an executable in the given PATH-style string. Returns the full path or null.</summary>
    internal static string? FindOnPath(string exeName, string? pathVariable)
    {
        if (string.IsNullOrWhiteSpace(exeName) || string.IsNullOrWhiteSpace(pathVariable)) return null;

        var candidates = Path.HasExtension(exeName)
            ? new[] { exeName }
            : new[] { exeName + ".exe", exeName };

        foreach (var dir in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var candidate in candidates)
            {
                try
                {
                    var full = Path.Combine(dir.Trim('"'), candidate);
                    if (File.Exists(full)) return full;
                }
                catch (ArgumentException)
                {
                    // A malformed PATH entry must not break the probe.
                }
            }
        }

        return null;
    }
}