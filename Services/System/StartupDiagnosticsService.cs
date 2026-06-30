using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using NullWave.Helpers.Logging;
using NullWave.Services;
using Serilog;

namespace NullWave.Services;

/// <summary>
/// Runs at application startup and logs a full diagnostic summary block.
///
/// Logged output example:
///   [STARTUP] ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
///   [STARTUP] NullWave v0.4.1 | .NET 8.0.x | OS: Linux 6.x
///   [STARTUP] Library: 42 tracks | DB: ~/.nullwave/library.db | Load: 18ms
///   [STARTUP] Key: YouTube      → loaded
///   [STARTUP] Key: LastFm       → loaded
///   [STARTUP] Key: SoundCloud   → missing
///   [STARTUP] Connectivity      → ok (latency: 142ms)
///   [STARTUP] VLC               → 3.0.23
///   [STARTUP] yt-dlp            → 2026.06.09
///   [STARTUP] ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
/// </summary>
public class StartupDiagnosticsService
{
    private readonly KeyStoreService _keyStore;
    private readonly LibraryService _library;

    private static readonly string[] KeyNames =
        { "YouTube", "LastFm", "SoundCloud", "Spotify:ClientId" };

    private static readonly string ConnectivityUrl =
        "https://www.last.fm";

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

        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nullwave", "library.db");

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

        //  4. Internet connectivity 
        await CheckConnectivityAsync();

        //  5. Tool versions 
        await LogToolVersionAsync("vlc", "--version", "VLC");
        await LogToolVersionAsync("yt-dlp", "--version", "yt-dlp");

        NullActionLogger.StartupLine(sep);
    }

    //  Helpers 

    private static async Task CheckConnectivityAsync()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var response = await client.SendAsync(
                new HttpRequestMessage(HttpMethod.Head, ConnectivityUrl));
            sw.Stop();

            var status = response.IsSuccessStatusCode ? "ok" : $"http {(int)response.StatusCode}";
            NullActionLogger.StartupLine(
                $"Connectivity          → {status} (latency: {sw.ElapsedMilliseconds}ms)");
        }
        catch (Exception ex)
        {
            sw.Stop();
            NullActionLogger.StartupLine(
                $"Connectivity          → failed ({ex.GetType().Name})");
        }
    }

    private static async Task LogToolVersionAsync(
        string command, string versionArg, string displayName)
    {
        try
        {
            var psi = new ProcessStartInfo(command, versionArg)
            {
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false
            };
            using var proc = Process.Start(psi);
            if (proc == null)
            {
                NullActionLogger.StartupLine($"{displayName,-20}→ not found");
                return;
            }
            var output = await proc.StandardOutput.ReadLineAsync()
                         ?? await proc.StandardError.ReadLineAsync()
                         ?? "unknown";
            await proc.WaitForExitAsync();

            // Take only first line, trim it
            var ver = output.Split('\n')[0].Trim();
            NullActionLogger.StartupLine($"{displayName,-20}→ {ver}");
        }
        catch
        {
            NullActionLogger.StartupLine($"{displayName,-20}→ not found");
        }
    }
}