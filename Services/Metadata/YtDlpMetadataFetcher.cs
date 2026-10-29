using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using NullWave.Helpers;
using NullWave.Services;
using NullWave.Services.Integration;
using NullWave.Models;
using Serilog;

namespace NullWave.Services.Metadata;

public class YtDlpMetadataFetcher
{
    private readonly string _sourceName;

    public YtDlpMetadataFetcher(string sourceName)
    {
        _sourceName = sourceName;
    }

    public async Task<(string Title, string Artist, string? ThumbnailPath, TimeSpan Duration)> FetchAsync(string url)
    {
        var prefs = PreferencesService.Shared.Current;
        if (!prefs.EnableYtDlp)
        {
            return (string.Empty, string.Empty, null, TimeSpan.Zero);
        }

        try
        {
            // Use artist,creator for Archive.org to avoid account names, fallback to uploader for others
            var artistField = _sourceName == "Archive.org" ? "%(artist,creator)s" : "%(artist,creator,uploader)s";
            
            var args = new List<string> {
                "--no-download", "--no-warnings", "--no-playlist", "--playlist-items", "1",
                "--print", "%(title)s",
                "--print", artistField,
                "--print", "%(thumbnail)s",
                "--print", "%(duration)s"
            };
            
            YtDlpArgs.AppendNetworkArgs(args, prefs);
            args.Add(url);

            var result = await ProcessRunner.RunAsync("yt-dlp", args.ToArray(), TimeSpan.FromSeconds(15), resolveViaPath: true);

            if (result.ExitCode != 0 || result.TimedOut || result.Canceled)
            {
                Log.Warning("[{Source}] metadata fetch failed or timed out for {Url}", _sourceName, url);
                return (string.Empty, string.Empty, null, TimeSpan.Zero); // Return empty so UI knows to retry/ignore
            }

            var lines = result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var title    = lines.Length > 0 && lines[0] != "NA" && lines[0] != "None" ? lines[0] : string.Empty;
            var artist   = lines.Length > 1 && lines[1] != "NA" && lines[1] != "None" ? lines[1] : string.Empty;
            var thumbUrl = lines.Length > 2 && lines[2] != "NA" && lines[2] != "None" ? lines[2] : null;

            var duration = TimeSpan.Zero;
            if (lines.Length > 3 && double.TryParse(lines[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var secs))
                duration = TimeSpan.FromSeconds(secs);

            string? thumbPath = null;
            if (!string.IsNullOrEmpty(thumbUrl))
            {
                var hashBytes = MD5.HashData(Encoding.UTF8.GetBytes(url));
                var hash = Convert.ToHexString(hashBytes)[..12];
                var prefix = _sourceName.ToLowerInvariant().Replace(" ", "").Replace(".", "");
                thumbPath = await ThumbnailDownloader.FetchAsync(thumbUrl, $"{prefix}_{hash}");
            }

            Log.Information("[{Source}] metadata fetched: {Title} by {Artist}", _sourceName, title, artist);
            return (title, artist, thumbPath, duration);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[{Source}] metadata fetch failed for {Url}", _sourceName, url);
            return (string.Empty, string.Empty, null, TimeSpan.Zero);
        }
    }
}