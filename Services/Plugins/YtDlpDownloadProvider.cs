using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NullWave.Models;
using Serilog;

namespace NullWave.Services.Plugins;

/// <summary>
/// Wraps DownloadService as an optional plugin. When yt-dlp is missing or disabled,
/// all download methods return graceful failures and the UI can hide download controls.
/// </summary>
public class YtDlpDownloadProvider : IDownloadProvider
{
    private readonly DownloadService _inner;
    private readonly PreferencesService _prefs;
    private static readonly TimeSpan PathCheckTimeout = TimeSpan.FromSeconds(3);

    public string Name => "yt-dlp Downloader";
    public string Description => "Downloads audio from YouTube, SoundCloud, and other supported sites";
    public PluginState State { get; set; } = PluginState.Unavailable;
    public bool IsEnabled { get; set; } = true;

    // I expose the inner service so MainViewModel can still wire events
    // (PlaylistBatchStarted, etc.) during the migration period.
    public DownloadService Inner => _inner;

    public YtDlpDownloadProvider(DownloadService inner, PreferencesService prefs)
    {
        _inner = inner;
        _prefs = prefs;
        IsEnabled = prefs.Current.EnableYtDlp;
    }

    public Task<bool> InitializeAsync(CancellationToken ct = default)
    {
        if (!IsEnabled)
        {
            State = PluginState.Disabled;
            Log.Information("[{Name}] Disabled by user preference", Name);
            return Task.FromResult(false);
        }

        if (!IsYtDlpOnPath())
        {
            State = PluginState.Unavailable;
            Log.Warning("[{Name}] yt-dlp not found on PATH — download features disabled", Name);
            return Task.FromResult(false);
        }

        State = PluginState.Available;
        Log.Information("[{Name}] yt-dlp detected — download provider ready", Name);
        return Task.FromResult(true);
    }

    public Task ShutdownAsync(CancellationToken ct = default)
    {
        _inner.CancelCurrentDownload();
        State = PluginState.Unavailable;
        return Task.CompletedTask;
    }

    public bool SupportsUrl(string url)
    {
        if (!IsEnabled || State != PluginState.Available)
            return false;

        return url.Contains("youtube", StringComparison.OrdinalIgnoreCase)
            || url.Contains("youtu.be", StringComparison.OrdinalIgnoreCase)
            || url.Contains("soundcloud", StringComparison.OrdinalIgnoreCase)
            || url.Contains("bandcamp", StringComparison.OrdinalIgnoreCase);
    }

    public Task<DownloadResult> DownloadAsync(string url, DownloadOptions options, CancellationToken ct = default)
    {
        if (!IsEnabled || State != PluginState.Available)
            return Task.FromResult(DownloadResult.Failed("yt-dlp provider not available"));

        // I generate a fresh track ID since the inner service expects one
        var trackId = Guid.NewGuid().ToString();

        _ = _inner.DownloadAsync(
            trackId: trackId,
            url: url,
            audioFormat: options.Format,
            audioQuality: options.Quality,
            allowPlaylist: false,
            isInteractive: true,
            ct: ct);

        // Note: DownloadService is fire-and-forget with events. I return success
        // here because the real result arrives via the ProgressChanged/DownloadCompleted
        // events on the Inner service.
        return Task.FromResult(DownloadResult.Succeeded(string.Empty));
    }

    private static bool IsYtDlpOnPath()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "yt-dlp",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            return proc?.WaitForExit((int)PathCheckTimeout.TotalMilliseconds) == true && proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}