using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace NullWave.Services;

public class DownloadService
{
    private readonly string _downloadDir;

    public event Action<string, float>? ProgressChanged;
    public event Action<string, string>? DownloadCompleted;
    public event Action<string, string>? DownloadFailed;

    private static readonly Regex ProgressRegex = new(
        @"\[download\]\s+([\d.]+)%", RegexOptions.Compiled);

    private readonly HashSet<string> _activeDownloads = new();
    private CancellationTokenSource? _currentDownloadCts;

    // ── Concurrency cap ──────────────────────────────────────────────────────
    // Max 5 hardcoded as the ceiling; the semaphore is rebuilt when the user
    // changes MaxConcurrentDownloads in settings via UpdateConcurrencyLimit().
    private SemaphoreSlim _semaphore = new(2, 5);
    private int _currentLimit = 2;

    /// <summary>
    /// Call this when the user changes MaxConcurrentDownloads in Settings.
    /// Rebuilds the semaphore with the new limit without cancelling active downloads.
    /// </summary>
    public void UpdateConcurrencyLimit(int newLimit)
    {
        newLimit = Math.Clamp(newLimit, 1, 5);
        if (newLimit == _currentLimit) return;

        // Dispose old semaphore and create a fresh one.
        // Any downloads currently waiting on the old semaphore will get
        // ObjectDisposedException, which we catch in DownloadAsync and treat as cancellation.
        var old = _semaphore;
        _semaphore = new SemaphoreSlim(newLimit, 5);
        _currentLimit = newLimit;
        old.Dispose();

        Log.Information("[DownloadService] Concurrency limit updated to {Limit}", newLimit);
    }

    public DownloadService()
    {
        _downloadDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nullwave", "downloads");
        Directory.CreateDirectory(_downloadDir);
    }

    public void CancelCurrentDownload()
    {
        _currentDownloadCts?.Cancel();
        Log.Debug("[DownloadService] Current download cancelled by caller");
    }

    public async Task DownloadAsync(
        string trackId,
        string url,
        string audioFormat = "mp3",
        string audioQuality = "best",
        CancellationToken ct = default)
    {
        var outputTemplate = Path.Combine(_downloadDir, "%(title)s.%(ext)s");

        var qualityValue = audioQuality switch
        {
            "best" => "0",
            "320"  => "0",
            "192"  => "2",
            "128"  => "4",
            "96"   => "6",
            _      => "0"
        };

        var args = new List<string>
        {
            url,
            "--extract-audio",
            "--audio-format", audioFormat,
            "--audio-quality", qualityValue,
            "--output", outputTemplate,
            "--no-playlist",
            "--print", "after_move:filepath"
        };

        lock (_activeDownloads)
        {
            if (_activeDownloads.Contains(url))
            {
                Log.Debug("[DownloadService] Skipping duplicate download for {Url}", url);
                return;
            }
            _activeDownloads.Add(url);
        }

        CancellationTokenSource cts;
        lock (this)
        {
            _currentDownloadCts?.Cancel();
            cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _currentDownloadCts = cts;
        }
        ct = cts.Token;

        Log.Information("Starting download: {Url} (format={Format}, quality={Quality})",
            url, audioFormat, audioQuality);

        // ── Acquire concurrency slot ─────────────────────────────────────────
        try
        {
            await _semaphore.WaitAsync(ct);
        }
        catch (ObjectDisposedException)
        {
            // Semaphore was rebuilt due to a settings change - just re-queue
            Log.Debug("[DownloadService] Semaphore rebuilt mid-wait, re-acquiring");
            await _semaphore.WaitAsync(ct);
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName               = "yt-dlp",
                Arguments              = string.Join(" ", args),
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true
            };

            using var process = new Process { StartInfo = psi };
            string? outputFilePath = null;

            process.OutputDataReceived += (_, e) =>
            {
                if (string.IsNullOrEmpty(e.Data)) return;

                if (e.Data.StartsWith("/") || e.Data.StartsWith("~"))
                {
                    outputFilePath = e.Data.Trim();
                    return;
                }

                var match = ProgressRegex.Match(e.Data);
                if (match.Success &&
                    float.TryParse(match.Groups[1].Value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var pct))
                {
                    ProgressChanged?.Invoke(trackId, pct / 100f);
                }

                Log.Debug("yt-dlp: {Line}", e.Data);
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    Log.Warning("yt-dlp stderr: {Line}", e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(ct);

            if (process.ExitCode == 0 && outputFilePath != null && File.Exists(outputFilePath))
            {
                Log.Information("Download complete: {Path}", outputFilePath);
                DownloadCompleted?.Invoke(trackId, outputFilePath);
            }
            else if (process.ExitCode == 0)
            {
                var recent = FindMostRecentDownload();
                if (recent != null)
                {
                    Log.Warning("[DownloadService] filepath not captured, using most recent: {Path}", recent);
                    DownloadCompleted?.Invoke(trackId, recent);
                }
                else
                {
                    Log.Error("[DownloadService] Download exited 0 but no output file found for {TrackId}", trackId);
                    DownloadFailed?.Invoke(trackId, "File not found after download");
                }
            }
            else
            {
                DownloadFailed?.Invoke(trackId, $"yt-dlp exited with code {process.ExitCode}");
            }
        }
        catch (OperationCanceledException)
        {
            Log.Warning("Download cancelled: {TrackId}", trackId);
            DownloadFailed?.Invoke(trackId, "Cancelled");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Download exception for {Url}", url);
            DownloadFailed?.Invoke(trackId, ex.Message);
        }
        finally
        {
            _semaphore.Release();
            lock (_activeDownloads)
                _activeDownloads.Remove(url);
        }
    }

    public async Task DownloadPlaylistAsync(
        string playlistUrl,
        Action<string, int, int>? onTrackStarted = null,
        Action<string, string, string>? onTrackCompleted = null,
        Action<string, string>? onTrackFailed = null,
        CancellationToken ct = default)
    {
        Log.Information("Starting playlist download: {Url}", playlistUrl);

        try
        {
            var metadataArgs = $"--flat-playlist --dump-json --no-download \"{playlistUrl}\"";
            var metadataPsi = new ProcessStartInfo
            {
                FileName               = "yt-dlp",
                Arguments              = metadataArgs,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true
            };

            using var metadataProc = new Process { StartInfo = metadataPsi };
            metadataProc.Start();
            var metadataOutput = await metadataProc.StandardOutput.ReadToEndAsync();
            await metadataProc.WaitForExitAsync(ct);

            if (metadataProc.ExitCode != 0)
            {
                Log.Error("Failed to fetch playlist metadata");
                return;
            }

            var tracks = new List<(string Title, string Artist, string Url)>();
            var lines = metadataOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    var title  = root.TryGetProperty("title",    out var t) ? t.GetString() ?? "Unknown" : "Unknown";
                    var artist = root.TryGetProperty("uploader", out var u) ? u.GetString() ?? "Unknown" : "Unknown";
                    var id     = root.TryGetProperty("id",       out var i) ? i.GetString() ?? ""        : "";
                    tracks.Add((title, artist, $"https://www.youtube.com/watch?v={id}"));
                }
                catch { /* skip malformed lines */ }
            }

            Log.Information("Playlist has {Count} tracks", tracks.Count);

            for (int i = 0; i < tracks.Count; i++)
            {
                if (ct.IsCancellationRequested) break;

                var (title, artist, url) = tracks[i];
                onTrackStarted?.Invoke(title, i + 1, tracks.Count);

                var trackId = Guid.NewGuid().ToString();
                var tcs = new TaskCompletionSource<bool>();

                void OnCompleted(string id, string filePath)
                {
                    if (id == trackId) { onTrackCompleted?.Invoke(title, artist, filePath); tcs.TrySetResult(true); }
                }
                void OnFailed(string id, string error)
                {
                    if (id == trackId) { onTrackFailed?.Invoke(title, error); tcs.TrySetResult(false); }
                }

                DownloadCompleted += OnCompleted;
                DownloadFailed    += OnFailed;
                await DownloadAsync(trackId, url, ct: ct);
                DownloadCompleted -= OnCompleted;
                DownloadFailed    -= OnFailed;

                await tcs.Task;
            }

            Log.Information("Playlist download complete");
        }
        catch (OperationCanceledException) { Log.Warning("Playlist download cancelled"); }
        catch (Exception ex) { Log.Error(ex, "Playlist download failed"); }
    }

    private string? FindMostRecentDownload()
    {
        var dir = new DirectoryInfo(_downloadDir);
        if (!dir.Exists) return null;
        FileInfo? newest = null;
        foreach (var file in dir.GetFiles("*.mp3"))
            if (newest == null || file.LastWriteTime > newest.LastWriteTime)
                newest = file;
        return newest?.FullName;
    }

    public string DownloadDirectory => _downloadDir;
}