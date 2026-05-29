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
}