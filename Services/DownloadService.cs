using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace NullWave.Services;

public class DownloadService
{
    private readonly string _downloadDir;

    public event Action<string, float>? ProgressChanged; // trackId, 0-1
    public event Action<string, string>? DownloadCompleted; // trackId, filePath
    public event Action<string, string>? DownloadFailed; // trackId, error

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
        string trackId, string url, CancellationToken ct = default)
    {
        var outputTemplate = Path.Combine(_downloadDir, "%(title)s.%(ext)s");

        var args = new List<string>
        {
            url,
            "--extract-audio",
            "--audio-format", "mp3",
            "--audio-quality", "0",
            "--output", outputTemplate,
            "--no-playlist",
            "--print", "after_move:filepath"
        };

        Log.Information("Starting download: {Url}", url);

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

                // Capture final file path
                if (e.Data.StartsWith("/") || e.Data.StartsWith("~"))
                {
                    outputFilePath = e.Data.Trim();
                    return;
                }

                // Parse progress percentage
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
                // Find the most recently downloaded file as fallback
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

    public static bool IsPlaylistUrl(string url)
{
    return url.Contains("list=", StringComparison.OrdinalIgnoreCase) ||
           url.Contains("/sets/", StringComparison.OrdinalIgnoreCase) ||   // SoundCloud playlists
           url.Contains("/playlist/", StringComparison.OrdinalIgnoreCase); // general
}

    public async Task DownloadPlaylistAsync(
        string playlistUrl,
        Action<string, int, int> onTrackStarted,        // title, index, total
        Action<string, string, string> onTrackCompleted,   // title, artist, filePath
        Action<string, string> onTrackFailed,           // title, error
        CancellationToken ct = default)
    {
        Log.Information("Fetching playlist metadata: {Url}", playlistUrl);

        // ── Step 1: get flat metadata (no download) ───────────────────────────
        var metaArgs = $"\"{playlistUrl}\" --flat-playlist --dump-json --no-warnings";
        var metaPsi = new ProcessStartInfo
        {
            FileName               = "yt-dlp",
            Arguments              = metaArgs,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true
        };

        var entries = new List<(string Id, string Title, string Artist)>();

        using (var metaProc = new Process { StartInfo = metaPsi })
        {
            metaProc.Start();
            string? line;
            while ((line = await metaProc.StandardOutput.ReadLineAsync()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    // Each line is a JSON object — extract id, title, and artist
                    var idMatch      = Regex.Match(line, @"""id""\s*:\s*""([^""]+)""");
                    var titleMatch   = Regex.Match(line, @"""title""\s*:\s*""([^""]+)""");
                    var channelMatch = Regex.Match(line, @"""channel""\s*:\s*""([^""]+)""");
                    var uploaderMatch = Regex.Match(line, @"""uploader""\s*:\s*""([^""]+)""");
                    if (idMatch.Success)
                    {
                        var id     = idMatch.Groups[1].Value;
                        var title  = titleMatch.Success  ? titleMatch.Groups[1].Value  : id;
                        var artist = channelMatch.Success ? channelMatch.Groups[1].Value
                                   : uploaderMatch.Success ? uploaderMatch.Groups[1].Value
                                   : "Unknown";
                        entries.Add((id, title, artist));
                    }
                }
                catch { /* skip malformed lines */ }
            }
            await metaProc.WaitForExitAsync(ct);
        }

        if (entries.Count == 0)
        {
            Log.Warning("Playlist returned 0 entries: {Url}", playlistUrl);
            onTrackFailed("playlist", "No tracks found in playlist");
            return;
        }

        Log.Information("Playlist has {Count} tracks", entries.Count);

        // ── Step 2: download each entry ───────────────────────────────────────
        for (int i = 0; i < entries.Count; i++)
        {
            if (ct.IsCancellationRequested) break;

            var (id, title, artist) = entries[i];
            var trackUrl = $"https://www.youtube.com/watch?v={id}";

            onTrackStarted(title, i + 1, entries.Count);
            Log.Information("Downloading playlist track {Index}/{Total}: {Title}", i + 1, entries.Count, title);

            var outputTemplate = Path.Combine(_downloadDir, "%(title)s.%(ext)s");
            var args = $"\"{trackUrl}\" --extract-audio --audio-format mp3 --audio-quality 0 " +
                    $"--output \"{outputTemplate}\" --no-playlist --print after_move:filepath --no-warnings";

            var psi = new ProcessStartInfo
            {
                FileName               = "yt-dlp",
                Arguments              = args,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true
            };

            try
            {
                using var proc = new Process { StartInfo = psi };
                string? filePath = null;

                proc.OutputDataReceived += (_, e) =>
                {
                    if (string.IsNullOrEmpty(e.Data)) return;
                    if (e.Data.StartsWith("/") || e.Data.StartsWith("~"))
                        filePath = e.Data.Trim();
                    Log.Debug("yt-dlp [{Title}]: {Line}", title, e.Data);
                };
                proc.ErrorDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        Log.Warning("yt-dlp stderr [{Title}]: {Line}", title, e.Data);
                };

                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
                await proc.WaitForExitAsync(ct);

                if (proc.ExitCode == 0 && filePath != null && File.Exists(filePath))
                    onTrackCompleted(title, artist, filePath);
                else if (proc.ExitCode == 0)
                {
                    var recent = FindMostRecentDownload();
                    if (recent != null) onTrackCompleted(title, artist, recent);
                    else onTrackFailed(title, "File not found after download");
                }
                else
                    onTrackFailed(title, $"yt-dlp exit code {proc.ExitCode}");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to download playlist track: {Title}", title);
                onTrackFailed(title, ex.Message);
            }
        }

        Log.Information("Playlist download complete: {Url}", playlistUrl);
    }
}