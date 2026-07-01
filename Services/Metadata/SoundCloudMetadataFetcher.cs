using System;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace NullWave.Services.Metadata;

public class SoundCloudMetadataFetcher
{
    public async Task<(string Title, string Artist, string? ThumbnailPath)> FetchAsync(string url)
    {
        try
        {
            var psi = new ProcessStartInfo("yt-dlp")
            {
                ArgumentList = 
                { 
                    "--no-download", 
                    "--print", "%(title)s", 
                    "--print", "%(uploader)s", 
                    "--print", "%(thumbnail)s", 
                    url 
                },
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true
            };

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var proc = Process.Start(psi);
            if (proc == null) return ("SoundCloud track", "Unknown", null);

            var outputTask = proc.StandardOutput.ReadToEndAsync(cts.Token);
            await proc.WaitForExitAsync(cts.Token);
            var output = await outputTask;

            var lines = output.Split('\n',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var title     = lines.Length > 0 ? lines[0] : "SoundCloud track";
            var artist    = lines.Length > 1 ? lines[1] : "Unknown";
            var thumbUrl  = lines.Length > 2 ? lines[2] : null;

            string? thumbPath = null;
            if (!string.IsNullOrEmpty(thumbUrl))
            {
                // Deterministic MD5 hash truncation for stable cache keys across restarts
                var hashBytes = MD5.HashData(Encoding.UTF8.GetBytes(url));
                var hash = Convert.ToHexString(hashBytes)[..12];
                thumbPath = await ThumbnailDownloader.FetchAsync(thumbUrl, $"sc_{hash}");
            }

            Log.Information("SoundCloud metadata fetched: {Title} by {Artist}", title, artist);
            return (title, artist, thumbPath);
        }
        catch (OperationCanceledException)
        {
            Log.Warning("SoundCloud metadata fetch timed out for {Url}", url);
            return ("SoundCloud track", "Unknown", null);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SoundCloud metadata fetch failed for {Url}", url);
            return ("SoundCloud track", "Unknown", null);
        }
    }
}