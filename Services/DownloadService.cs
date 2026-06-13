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

    public event Action<string, float>? ProgressChanged;    // trackId, 0-1
    public event Action<string, string>? DownloadCompleted; // trackId, filePath
    public event Action<string, string>? DownloadFailed;    // trackId, error

    private static readonly Regex ProgressRegex = new(
        @"\[download\]\s+([\d.]+)%", RegexOptions.Compiled);

    public DownloadService()
    {
        _downloadDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nullwave", "downloads");
        Directory.CreateDirectory(_downloadDir);
    }

    public async Task DownloadAsync(
        string trackId,
        string url,
        string audioFormat = "mp3",
        string audioQuality = "best",
        CancellationToken ct = default)
    {
        var outputTemplate = Path.Combine(_downloadDir, "%(title)s.%(ext)s");

        // Map UI quality names to yt-dlp quality values
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

        Log.Information("Starting download: {Url} (format={Format}, quality={Quality})",
            url, audioFormat, audioQuality);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "yt-dlp",
                Arguments = string.Join(" ", args),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
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

            if (process.ExitCode == 0 && outputFilePath != null &&
                File.Exists(outputFilePath))
            {
                Log.Information("Download complete: {Path}", outputFilePath);
                DownloadCompleted?.Invoke(trackId, outputFilePath);
            }
            else if (process.ExitCode == 0)
            {
                var recent = FindMostRecentDownload();
                if (recent != null)
                {
                    Log.Information("Download complete (fallback path): {Path}", recent);
                    DownloadCompleted?.Invoke(trackId, recent);
                }
                else
                {
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
    }

    // NEW: Playlist download support
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
            // Step 1: Get playlist metadata (flat list, no download)
            var metadataArgs = $"--flat-playlist --dump-json --no-download \"{playlistUrl}\"";
            var metadataPsi = new ProcessStartInfo
            {
                FileName = "yt-dlp",
                Arguments = metadataArgs,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
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

            // Parse JSON lines
            var tracks = new List<(string Title, string Artist, string Url)>();
            var lines = metadataOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            
            foreach (var line in lines)
            {
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    
                    var title = root.TryGetProperty("title", out var t) 
                        ? t.GetString() ?? "Unknown" 
                        : "Unknown";
                    var artist = root.TryGetProperty("uploader", out var u) 
                        ? u.GetString() ?? "Unknown" 
                        : "Unknown";
                    var id = root.TryGetProperty("id", out var i) 
                        ? i.GetString() ?? "" 
                        : "";
                    var url = $"https://www.youtube.com/watch?v={id}";
                    
                    tracks.Add((title, artist, url));
                }
                catch
                {
                    // Skip malformed lines
                }
            }

            Log.Information("Playlist has {Count} tracks", tracks.Count);

            // Step 2: Download each track
            for (int i = 0; i < tracks.Count; i++)
            {
                if (ct.IsCancellationRequested) break;

                var (title, artist, url) = tracks[i];
                onTrackStarted?.Invoke(title, i + 1, tracks.Count);

                var trackId = Guid.NewGuid().ToString();
                var tcs = new TaskCompletionSource<bool>();

                // Wire up completion events for this track
                void OnCompleted(string id, string filePath)
                {
                    if (id == trackId)
                    {
                        onTrackCompleted?.Invoke(title, artist, filePath);
                        tcs.TrySetResult(true);
                    }
                }

                void OnFailed(string id, string error)
                {
                    if (id == trackId)
                    {
                        onTrackFailed?.Invoke(title, error);
                        tcs.TrySetResult(false);
                    }
                }

                DownloadCompleted += OnCompleted;
                DownloadFailed += OnFailed;

                await DownloadAsync(trackId, url, ct: ct);

                DownloadCompleted -= OnCompleted;
                DownloadFailed -= OnFailed;

                await tcs.Task;
            }

            Log.Information("Playlist download complete");
        }
        catch (OperationCanceledException)
        {
            Log.Warning("Playlist download cancelled");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Playlist download failed");
        }
    }

    private string? FindMostRecentDownload()
    {
        var dir = new DirectoryInfo(_downloadDir);
        if (!dir.Exists) return null;

        FileInfo? newest = null;
        foreach (var file in dir.GetFiles("*.mp3"))
        {
            if (newest == null || file.LastWriteTime > newest.LastWriteTime)
                newest = file;
        }

        return newest?.FullName;
    }

    public string DownloadDirectory => _downloadDir;
}