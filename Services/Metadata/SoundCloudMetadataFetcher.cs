using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using NullWave.Helpers;
using NullWave.Services.Integration;
using Serilog;

namespace NullWave.Services.Metadata;

public class SoundCloudMetadataFetcher
{
    public async Task<(string Title, string Artist, string? ThumbnailPath, TimeSpan Duration)> FetchAsync(string url)
    {
        try
        {
            var args = new[] {
                "--no-download",
                "--print", "%(title)s",
                "--print", "%(uploader)s",
                "--print", "%(thumbnail)s",
                "--print", "%(duration)s",
                url
            };

            var result = await ProcessRunner.RunAsync("yt-dlp", args, TimeSpan.FromSeconds(15));

            if (result.ExitCode != 0 || result.TimedOut || result.Canceled)
            {
                Log.Warning("SoundCloud metadata fetch failed or timed out for {Url}", url);
                return ("SoundCloud track", "Unknown", null, TimeSpan.Zero);
            }

            var lines = result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

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
        catch (Exception ex)
        {
            Log.Error(ex, "SoundCloud metadata fetch failed for {Url}", url);
            return ("SoundCloud track", "Unknown", null, TimeSpan.Zero);
        }
    }
}