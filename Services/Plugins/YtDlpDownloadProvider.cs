using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NullWave.Models;
using NullWave.Helpers;
using Serilog;

namespace NullWave.Services.Plugins;

public class YtDlpDownloadProvider : IDownloadProvider
{
    private readonly DownloadService _inner;
    private readonly PreferencesService _prefs;
    private readonly Func<Task<string?>> _versionProbe;

    public string Name => "yt-dlp Downloader";
    public string Description => "Downloads audio from YouTube, SoundCloud, and other supported sites";
    public PluginState State { get; set; } = PluginState.Unavailable;
    public bool IsEnabled { get; set; } = true;

    public DownloadService Inner => _inner;

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

        // FIX: Use SourceDetector so Audiomack, Archive.org, and Mixcloud are recognized
        return SourceDetector.IsYtDlpSource(SourceDetector.Detect(url));
    }

    public Task<DownloadResult> DownloadAsync(string url, DownloadOptions options, CancellationToken ct = default)
    {
        if (!IsEnabled || State != PluginState.Available)
            return Task.FromResult(DownloadResult.Failed("yt-dlp provider not available"));

        var trackId = Guid.NewGuid().ToString();

        _ = _inner.DownloadAsync(
            trackId: trackId,
            url: url,
            audioFormat: options.Format,
            audioQuality: options.Quality,
            allowPlaylist: false,
            isInteractive: true,
            ct: ct);

        return Task.FromResult(DownloadResult.Succeeded(string.Empty));
    }

    public ProviderDescriptor Descriptor { get; } = new(
        Id: "yt-dlp",
        Kind: ProviderKind.Download,
        DisplayName: $"yt-dlp ({string.Join(", ", SupportedSites.All.Where(s => s.IsYtDlpNative).Select(s => s.DisplayName))})"
    );
}