using System;
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
    private readonly Func<Task<string?>> _versionProbe;

    public string Name => "yt-dlp Downloader";
    public string Description => "Downloads audio from YouTube, SoundCloud, and other supported sites";
    public PluginState State { get; set; } = PluginState.Unavailable;
    public bool IsEnabled { get; set; } = true;

    // I expose the inner service so MainViewModel can still wire events
    // (PlaylistBatchStarted, etc.) during the migration period.
    public DownloadService Inner => _inner;

    /// <param name="versionProbe">
    /// Asks which yt-dlp version is installed (null = not found). Defaults to the shared
    /// <see cref="ToolVersionProbe"/>, which the startup diagnostics also use, so yt-dlp is looked
    /// up once per run instead of being launched again for every check.
    /// </param>
    public YtDlpDownloadProvider(DownloadService inner, PreferencesService prefs, Func<Task<string?>>? versionProbe = null)
    {
        _inner = inner;
        _prefs = prefs;
        _versionProbe = versionProbe ?? (() => ToolVersionProbe.GetVersionAsync("yt-dlp"));
        IsEnabled = prefs.Current.EnableYtDlp;
    }

    public async Task<bool> InitializeAsync(CancellationToken ct = default)
    {
        if (!IsEnabled)
        {
            State = PluginState.Disabled;
            Log.Information("[{Name}] Disabled by user preference", Name);
            return false;
        }

        ct.ThrowIfCancellationRequested();
        var version = await _versionProbe();

        if (version is null)
        {
            State = PluginState.Unavailable;
            Log.Warning("[{Name}] yt-dlp not found - download features disabled", Name);
            return false;
        }

        State = PluginState.Available;
        Log.Information("[{Name}] yt-dlp {Version} detected - download provider ready", Name, version);
        return true;
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
}