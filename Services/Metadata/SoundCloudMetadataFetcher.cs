using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Serilog;

namespace NullWave.Services.Metadata;

public class SoundCloudMetadataFetcher
{
    public async Task<(string Title, string Artist, string? ThumbnailPath)> FetchAsync(string url)
    {
        try
        {
            var psi = new ProcessStartInfo(
                "yt-dlp",
                $"--no-download --print \"%(title)s\" --print \"%(uploader)s\" --print \"%(thumbnail)s\" \"{url}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return ("SoundCloud track", "Unknown", null);

            var output = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();

            var lines = output.Split('\n',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var title     = lines.Length > 0 ? lines[0] : "SoundCloud track";
            var artist    = lines.Length > 1 ? lines[1] : "Unknown";
            var thumbUrl  = lines.Length > 2 ? lines[2] : null;

            string? thumbPath = null;
            if (!string.IsNullOrEmpty(thumbUrl))
                thumbPath = await ThumbnailDownloader.FetchAsync(thumbUrl, $"sc_{url.GetHashCode():X8}");

            Log.Information("SoundCloud metadata fetched: {Title} by {Artist}", title, artist);
            return (title, artist, thumbPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SoundCloud metadata fetch failed for {Url}", url);
            return ("SoundCloud track", "Unknown", null);
        }
    }
}