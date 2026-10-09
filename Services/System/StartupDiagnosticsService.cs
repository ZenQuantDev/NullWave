using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using NullWave.Helpers;
using NullWave.Helpers.Logging;
using NullWave.Services.Security;

namespace NullWave.Services;

/// <summary>
/// Runs at application startup and logs a diagnostic summary block.
///
/// Logged output example:
///   [STARTUP] ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
///   [STARTUP] NullWave v0.6.3 | .NET 8.0.x | OS: Windows 10.0.x (X64)
///   [STARTUP] Library: 42 tracks | DB: ...\library.db | Load: 18ms
///   [STARTUP] Key: YouTube      → loaded
///   [STARTUP] Key: LastFm       → loaded
///   [STARTUP] Key: SoundCloud   → missing
///   [STARTUP] VLC               → 3.0.23
///   [STARTUP] yt-dlp            → 2026.08.19
///   [STARTUP] ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
///
/// Nothing here contacts the network. It used to send a request to last.fm on every launch, even
/// for people who never configured Last.fm, which does not fit a local-first app. If a connection
/// test is wanted, make it a button the user presses.
/// </summary>
public class StartupDiagnosticsService
{
    private readonly KeyStoreService _keyStore;
    private readonly LibraryService _library;

    private static readonly string[] KeyNames =
        { "YouTube", "LastFm", "SoundCloud", "Spotify:ClientId" };

    public StartupDiagnosticsService(KeyStoreService keyStore, LibraryService library)
    {
        _keyStore = keyStore;
        _library = library;
    }

    public async Task RunAsync()
    {
        var sep = new string('━', 51);
        NullActionLogger.StartupLine(sep);

        //  1. App identity
        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "unknown";

        var runtime = RuntimeInformation.FrameworkDescription;
        var os      = $"{RuntimeInformation.OSDescription.Trim()} " +
                      $"({RuntimeInformation.OSArchitecture})";

        NullActionLogger.StartupLine(
            $"NullWave v{version} | {runtime} | OS: {os}");

        //  2. Library load
        var sw = Stopwatch.StartNew();
        var allTracks = _library.GetAll();
        sw.Stop();

        var dbPath = NullWavePaths.DatabasePath;

        NullActionLogger.StartupLine(
            $"Library: {allTracks.Count} tracks | DB: {dbPath} | Load: {sw.ElapsedMilliseconds}ms");

        //  3. API key status
        foreach (var key in KeyNames)
        {
            string status;
            try
            {
                var val = _keyStore.GetKey(key);
                status = string.IsNullOrWhiteSpace(val) ? "missing" : "loaded";
            }
            catch (Exception ex)
            {
                status = $"decryption_failed ({ex.GetType().Name})";
            }

            NullActionLogger.StartupLine($"Key: {key,-20}→ {status}");
        }

        //  4. Tool versions (no network; no process on Windows)
        NullActionLogger.StartupLine($"{"VLC",-20}→ {await GetVlcVersionAsync() ?? "not found"}");
        NullActionLogger.StartupLine($"{"yt-dlp",-20}→ {await ToolVersionProbe.GetVersionAsync("yt-dlp") ?? "not found"}");

        NullActionLogger.StartupLine(sep);
    }

    private static async Task<string?> GetVlcVersionAsync()
    {
        // VLC on Windows allocates its own console when printing --version, producing a
        // "Press RETURN to continue..." popup that also hangs startup until Enter is pressed.
        // Read the version from the executable's file properties instead: same answer, no console.
        if (NullWavePaths.IsWindows)
        {
            var dir = PlatformHelper.ResolveVlcDirectory();
            if (dir == null) return null;

            try
            {
                var info = FileVersionInfo.GetVersionInfo(Path.Combine(dir, "vlc.exe"));
                return info.FileVersion ?? info.ProductVersion;
            }
            catch
            {
                return null;
            }
        }

        return await ToolVersionProbe.GetVersionAsync("vlc");
    }
}