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
    private readonly PreferencesService _prefsService;

    // FIX: Accept an optional PreferencesService for testability
    public YtDlpMetadataFetcher(string sourceName, PreferencesService? prefs = null)
    {
        _sourceName = sourceName;
        _prefsService = prefs ?? PreferencesService.Shared;
    }

    public async Task<(string Title, string Artist, string? ThumbnailPath, TimeSpan Duration)> FetchAsync(string url)
    {
        // Use the injected service instead of the global singleton
        var prefs = _prefsService.Current;
        if (!prefs.EnableYtDlp)
        {
            return (string.Empty, string.Empty, null, TimeSpan.Zero);
        }

        try
        {
            // Mixcloud's 'artist' is the tracklist, so use the DJ (uploader). Archive.org avoids account names.
            var artistField = _sourceName switch
            {
                "Archive.org" => "%(artist,creator)s",
                "Mixcloud"    => "%(uploader,creator)s",
                _             => "%(artist,creator,uploader)s",
            };
            
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
                return (string.Empty, string.Empty, null, TimeSpan.Zero); 
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