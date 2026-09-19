using System;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NullWave.Helpers;
using Serilog;

namespace NullWave.Services.Metadata;

public class SoundCloudMetadataFetcher
{
    public async Task<(string Title, string Artist, string? ThumbnailPath, TimeSpan Duration)> FetchAsync(string url)
    {
        try
        {
            var psi = new ProcessStartInfo(PlatformHelper.ResolveExecutable("yt-dlp"))
            {
                ArgumentList =
                {
                    "--no-download",
                    "--print", "%(title)s",
                    "--print", "%(uploader)s",
                    "--print", "%(thumbnail)s",
                    "--print", "%(duration)s",
                    url
                },
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true
            };

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var proc = Process.Start(psi);
            if (proc == null) return ("SoundCloud track", "Unknown", null, TimeSpan.Zero);

            var outputTask = proc.StandardOutput.ReadToEndAsync(cts.Token);
            await proc.WaitForExitAsync(cts.Token);
            var output = await outputTask;

            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var title    = lines.Length > 0 ? lines[0] : "SoundCloud track";
            var artist   = lines.Length > 1 ? lines[1] : "Unknown";
            var thumbUrl = lines.Length > 2 ? lines[2] : null;

            var duration = TimeSpan.Zero;
            if (lines.Length > 3 && double.TryParse(lines[3], out var secs))
                duration = TimeSpan.FromSeconds(secs);

            string? thumbPath = null;
            if (!string.IsNullOrEmpty(thumbUrl))
            {
                var hashBytes = MD5.HashData(Encoding.UTF8.GetBytes(url));
                var hash = Convert.ToHexString(hashBytes)[..12];
                thumbPath = await ThumbnailDownloader.FetchAsync(thumbUrl, $"sc_{hash}");
            }

            Log.Information("SoundCloud metadata fetched: {Title} by {Artist}", title, artist);
            return (title, artist, thumbPath, duration);
        }
        catch (OperationCanceledException)
        {
            Log.Warning("SoundCloud metadata fetch timed out for {Url}", url);
            return ("SoundCloud track", "Unknown", null, TimeSpan.Zero);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SoundCloud metadata fetch failed for {Url}", url);
            return ("SoundCloud track", "Unknown", null, TimeSpan.Zero);
        }
    }
}