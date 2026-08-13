using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using NullWave.Helpers;
using NullWave.Helpers.Logging;
using NullWave.Services;
using NullWave.Services.Integration;
using NullWave.Services.Plugins;
using NullWave.Services.SmartSorting;
using NullWave.ViewModels.Base;
using NullWave.Models;
using Serilog;

namespace NullWave.Services;

public class DownloadService
{
    private readonly string _downloadDir;
    private readonly LibraryService _libraryService;
    private readonly PreferencesService _prefsService;
    private readonly AlbumArtService _albumArtService;

    public event Action<string, float>? ProgressChanged;
    public event Action<string, string, bool>? DownloadCompleted;
    public event Action<string, string, bool>? DownloadFailed;

    public event Action<int>? PlaylistBatchStarted;
    public event Action<int, int, int>? PlaylistBatchProgress;
    public event Action<int, int, int>? PlaylistBatchCompleted;

    private static readonly Regex ProgressRegex = new(
        @"\[download\]\s+([\d.]+)%", RegexOptions.Compiled);
    private static readonly Regex TopicSuffixRegex = new(
        @"\s*-\s*Topic\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    
    // FIX: Safe regexes to strip ONLY the ?si= tracking parameter
    private static readonly Regex SiParamRegex1 = new(@"&si=[^&]*", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SiParamRegex2 = new(@"\?si=[^&]*&", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SiParamRegex3 = new(@"\?si=[^&]*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly HashSet<string> _activeDownloads = new();
    private CancellationTokenSource? _currentDownloadCts;
    private SemaphoreSlim _semaphore = new(2, 5);
    private int _currentLimit = 2;

    private static bool? _aria2cAvailable;
    private static readonly object _aria2cLock = new();

    public void UpdateConcurrencyLimit(int newLimit)
    {
        newLimit = Math.Clamp(newLimit, 1, 5);
        if (newLimit == _currentLimit) return;

        var old = _semaphore;
        _semaphore = new SemaphoreSlim(newLimit, 5);
        _currentLimit = newLimit;
        old.Dispose();
        Log.Information("[DownloadService] Concurrency limit updated to {Limit}", newLimit);
    }

    public DownloadService(LibraryService libraryService, PreferencesService prefsService, AlbumArtService albumArtService)
    {
        _libraryService = libraryService;
        _prefsService = prefsService;
        _albumArtService = albumArtService;
        _downloadDir = NullWavePaths.DownloadsDir;
        Directory.CreateDirectory(_downloadDir);
    }

    public void CancelCurrentDownload()
    {
        _currentDownloadCts?.Cancel();
        Log.Debug("[DownloadService] Current download cancelled by caller");
    }

    private static bool IsAria2cAvailable()
    {
        if (_aria2cAvailable.HasValue) return _aria2cAvailable.Value;

        lock (_aria2cLock)
        {
            if (_aria2cAvailable.HasValue) return _aria2cAvailable.Value;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = PlatformHelper.ResolveExecutable("aria2c"),
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit(2000);
                _aria2cAvailable = proc != null && proc.ExitCode == 0;
            }
            catch
            {
                _aria2cAvailable = false;
            }

            if (!_aria2cAvailable.Value)
                Log.Information("[DownloadService] aria2c not found on PATH — using yt-dlp's native downloader");
            else
                Log.Information("[DownloadService] aria2c detected — enabling multi-connection downloads");

            return _aria2cAvailable.Value;
        }
    }

    private void AppendSpeedAndAuthArgs(List<string> args)
    {
        if (_prefsService.Current.UseAria2c && IsAria2cAvailable())
        {
            args.Add("--downloader");
            args.Add("aria2c");
            args.Add("--downloader-args");
            args.Add("aria2c:-x 16 -k 1M -s 16");
        }

        var browserCookies = _prefsService.Current.YtDlpBrowserCookies;
        if (!string.IsNullOrWhiteSpace(browserCookies))
        {
            args.Add("--cookies-from-browser");
            args.Add(browserCookies);
        }
    }

    private int GetThrottleDelayMs()
    {
        return string.IsNullOrWhiteSpace(_prefsService.Current.YtDlpBrowserCookies)
            ? Random.Shared.Next(3000, 8000)
            : Random.Shared.Next(600, 1800);
    }

    public async Task DownloadAsync(
        string trackId,
        string url,
        string audioFormat = "mp3",
        string audioQuality = "best",
        bool allowPlaylist = false,
        bool isInteractive = true,
        CancellationToken ct = default)
    {
        // FIX: Safely strip YouTube share tracking parameters (?si=...) 
        // without destroying required ?v= or ?list= parameters.
        if (url.Contains("youtu.be") || url.Contains("youtube.com"))
        {
            url = SiParamRegex1.Replace(url, "");
            url = SiParamRegex2.Replace(url, "?");
            url = SiParamRegex3.Replace(url, "");
        }

        if (!allowPlaylist && (url.Contains("list=") || url.Contains("playlist?")))
        {
            Log.Warning("[DownloadService] Blocked playlist URL in single-track pipeline: {Url}", url);
            DownloadFailed?.Invoke(trackId, "Playlist URLs are not supported in single-track mode", isInteractive);
            return;
        }

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
            "-f", "bestaudio/best",
            "--output", outputTemplate,
            "--print", "after_move:filepath",
            "--ignore-errors",
            "--js-runtimes", "node",
            "--remote-components", "ejs:github",
            "--embed-metadata",
            "--embed-thumbnail",
            "--write-thumbnail",
            "--parse-metadata", "uploader:%(artist)s",
            "--parse-metadata", "channel:%(artist)s"
        };

        AppendSpeedAndAuthArgs(args);

        if (!allowPlaylist)
        {
            args.Add("--no-playlist");
        }
        else
        {
            args.Add("--yes-playlist");
        }

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
            if (isInteractive)
            {
                _currentDownloadCts?.Cancel();
                cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                _currentDownloadCts = cts;
            }
            else
            {
                cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            }
        }

        ct = cts.Token;
        Log.Debug("Starting download: {Url} (format={Format}, quality={Quality})",
            url, audioFormat, audioQuality);

        try
        {
            await _semaphore.WaitAsync(ct);
        }
        catch (ObjectDisposedException)
        {
            Log.Debug("[DownloadService] Semaphore rebuilt mid-wait, re-acquiring");
            await _semaphore.WaitAsync(ct);
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName               = PlatformHelper.ResolveExecutable("yt-dlp"),
                Arguments              = string.Join(" ", args.Select(QuoteIfNeeded)),
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true
            };

            using var process = new Process { StartInfo = psi };
            string? outputFilePath = null;

            process.OutputDataReceived += (_, e) =>
            {
                if (string.IsNullOrWhiteSpace(e.Data)) return;
                var line = e.Data.Trim();

                var match = ProgressRegex.Match(line);
                if (match.Success &&
                    float.TryParse(match.Groups[1].Value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var pct))
                {
                    ProgressChanged?.Invoke(trackId, pct / 100f);
                    return;
                }

                if (line.StartsWith("/") || line.StartsWith("~") || Regex.IsMatch(line, @"^[A-Za-z]:[\\/]"))
                {
                    outputFilePath = line;
                    return;
                }

                Log.Debug("yt-dlp: {Line}", line);
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    Log.Debug("yt-dlp stderr: {Line}", e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync(ct);
            process.WaitForExit();

            if (process.ExitCode == 0 && outputFilePath != null && File.Exists(outputFilePath))
            {
                Log.Debug("Download complete: {Path}", outputFilePath);
                DownloadCompleted?.Invoke(trackId, outputFilePath, isInteractive);
            }
            else if (process.ExitCode == 0)
            {
                var recent = FindMostRecentUnlinkedDownload();
                if (recent != null)
                {
                    Log.Warning("[DownloadService] filepath not captured, using most recent: {Path}", recent);
                    DownloadCompleted?.Invoke(trackId, recent, isInteractive);
                }
                else
                {
                    Log.Error("[DownloadService] Download exited 0 but no output file found for {TrackId}", trackId);
                    DownloadFailed?.Invoke(trackId, "File not found after download", isInteractive);
                }
            }
            else
            {
                DownloadFailed?.Invoke(trackId, $"yt-dlp exited with code {process.ExitCode}", isInteractive);
            }
        }
        catch (OperationCanceledException)
        {
            Log.Warning("Download cancelled: {TrackId}", trackId);
            DownloadFailed?.Invoke(trackId, "Cancelled", isInteractive);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Download exception for {Url}", url);
            DownloadFailed?.Invoke(trackId, ex.Message, isInteractive);
        }
        finally
        {
            _semaphore.Release();
            lock (_activeDownloads)
                _activeDownloads.Remove(url);
        }
    }

    private static string QuoteIfNeeded(string arg)
    {
        if (arg.Contains(' ') && !arg.StartsWith("\""))
            return $"\"{arg}\"";
        return arg;
    }

    public async Task DownloadPlaylistAsync(
        string playlistUrl,
        Action<Track>? onTrackReady = null,
        Action<string, int, int>? onTrackStarted = null,
        Action<string, string, string>? onTrackCompleted = null,
        Action<string, string>? onTrackFailed = null,
        CancellationToken ct = default)
    {
        Log.Information("Starting playlist download: {Url}", playlistUrl);
        try
        {
            var metadataArgs = $"--flat-playlist --dump-json --ignore-errors --no-download --js-runtimes node --remote-components ejs:github \"{playlistUrl}\"";
            var metadataPsi = new ProcessStartInfo
            {
                FileName               = PlatformHelper.ResolveExecutable("yt-dlp"),
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

            var tracks = new List<(string Title, string Artist, string Url, string VideoId)>();
            string[] lines = metadataOutput.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(line);
                    var root = doc.RootElement;

                    // FIX: Parse the title ONCE at the top of the scope
                    var rawTitle = root.TryGetProperty("title", out var titleProp)
                        ? titleProp.GetString() ?? "Unknown Track" : "Unknown Track";

                    string videoId = root.TryGetProperty("id", out var idProp)
                        ? (idProp.GetString() ?? "") : "";

                    // Fallback to extract video ID from the "url" property if "id" is missing
                    if (string.IsNullOrEmpty(videoId) && root.TryGetProperty("url", out var urlProp))
                    {
                        var rawUrl = urlProp.GetString() ?? "";
                        var vMatch = Regex.Match(rawUrl, @"[?&]v=([^&]+)");
                        if (vMatch.Success) videoId = vMatch.Groups[1].Value;
                        else if (rawUrl.Contains("youtu.be/")) videoId = rawUrl.Split("youtu.be/")[1].Split('?')[0];
                    }

                    // Skip tracks entirely if we still can't find a video ID
                    if (string.IsNullOrEmpty(videoId))
                    {
                        Log.Warning("[DownloadService] Skipping playlist track with missing video ID: {Title}", rawTitle);
                        continue; 
                    }

                    // Continue with artist parsing...
                    string artist = "Unknown Artist";
                    if (root.TryGetProperty("artist", out var artistProp) &&
                        !string.IsNullOrWhiteSpace(artistProp.GetString()))
                    {
                        artist = artistProp.GetString()!.Trim();
                    }
                    else if (root.TryGetProperty("creator", out var creatorProp) &&
                        !string.IsNullOrWhiteSpace(creatorProp.GetString()))
                    {
                        artist = creatorProp.GetString()!.Trim();
                    }
                    else if (root.TryGetProperty("uploader", out var uploaderProp) &&
                        !string.IsNullOrWhiteSpace(uploaderProp.GetString()))
                    {
                        artist = uploaderProp.GetString()!.Trim();
                    }
                    else if (root.TryGetProperty("channel", out var channelProp) &&
                        !string.IsNullOrWhiteSpace(channelProp.GetString()))
                    {
                        artist = channelProp.GetString()!.Trim();
                    }

                    if (_prefsService.Current.AutoCleanMetadata)
                    {
                        artist = TopicSuffixRegex.Replace(artist, string.Empty).Trim();
                    }

                    string cleanTitle = rawTitle;
                    if (string.IsNullOrWhiteSpace(artist) ||
                        artist.Equals("Unknown Artist", StringComparison.OrdinalIgnoreCase))
                    {
                        var parsed = NullWave.Services.Metadata.TrackTitleParser
                            .TryParseArtistTitle(rawTitle);

                        if (parsed != null)
                        {
                            artist     = parsed.Value.Artist;
                            cleanTitle = parsed.Value.Title;
                        }
                    }

                    var trackUrl = $"https://www.youtube.com/watch?v={videoId}";
                    tracks.Add((cleanTitle, artist, trackUrl, videoId));
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to parse playlist entry JSON");
                }
            }

            Log.Information("Playlist has {Count} tracks", tracks.Count);
            PlaylistBatchStarted?.Invoke(tracks.Count);

            int completedCount = 0;
            int failedCount = 0;
            int skippedCount = 0;

            for (int i = 0; i < tracks.Count; i++)
            {
                if (ct.IsCancellationRequested) break;

                var (title, artist, url, videoId) = tracks[i];
                onTrackStarted?.Invoke(title, i + 1, tracks.Count);

                var trackId = Guid.NewGuid();
                var cleanUrl = url.Split('&')[0];
                string? finalFilePath = null;

                var newTrack = new Track
                {
                    Id = trackId,
                    Title = title ?? "Unknown Track",
                    Artist = artist ?? "Unknown Artist",
                    Url = cleanUrl,
                    Source = TrackSource.YouTube,
                    AlbumArtPath = $"https://img.youtube.com/vi/{videoId}/hqdefault.jpg",
                    DateAdded = DateTime.UtcNow
                };

                if (_prefsService.Current.PreventDuplicateDownloads && _libraryService.IsDuplicate(newTrack))
                {
                    Log.Debug("[DownloadService] Skipped duplicate: {Artist} - {Title}", artist, title);
                    skippedCount++;
                    PlaylistBatchProgress?.Invoke(completedCount, tracks.Count, skippedCount);
                    continue;
                }

                _libraryService.Add(newTrack);

                var tcs = new TaskCompletionSource<bool>();

                System.Action<string, string, bool> OnCompleted = (id, filePath, isInteractive) =>
                {
                    if (id == trackId.ToString())
                    {
                        finalFilePath = filePath;
                        onTrackCompleted?.Invoke(title ?? "Unknown", artist ?? "Unknown", filePath);
                        tcs.TrySetResult(true);
                    }
                };

                System.Action<string, string, bool> OnFailed = (id, error, isInteractive) =>
                {
                    if (id == trackId.ToString())
                    {
                        onTrackFailed?.Invoke(title ?? "Unknown", error);
                        tcs.TrySetResult(false);
                    }
                };

                try
                {
                    DownloadCompleted += OnCompleted;
                    DownloadFailed    += OnFailed;

                    await DownloadAsync(trackId.ToString(), cleanUrl, audioFormat: "mp3", audioQuality: "best", allowPlaylist: false, isInteractive: false, ct: ct);

                    DownloadCompleted -= OnCompleted;
                    DownloadFailed    -= OnFailed;

                    var success = await tcs.Task;

                    if (!success)
                    {
                        _libraryService.Remove(trackId);
                        failedCount++;
                    }
                    else if (!string.IsNullOrEmpty(finalFilePath))
                    {
                        completedCount++;
                        var dbTrack = _libraryService.GetAll().FirstOrDefault(t => t.Id == trackId);

                        if (dbTrack != null)
                        {
                            dbTrack.FilePath = finalFilePath;

                            if (_prefsService.Current.AutoCleanMetadata && !string.IsNullOrEmpty(dbTrack.Artist))
                            {
                                var cleanArtist = System.Text.RegularExpressions.Regex.Replace(
                                    dbTrack.Artist,
                                    @"\s*-\s*Topic\s*$",
                                    string.Empty,
                                    System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();

                                if (string.IsNullOrWhiteSpace(cleanArtist) ||
                                    cleanArtist.Equals("Unknown Artist", StringComparison.OrdinalIgnoreCase) ||
                                    cleanArtist.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                                {
                                    var parsed = NullWave.Services.Metadata.TrackTitleParser
                                        .TryParseArtistTitle(dbTrack.Title);

                                    if (parsed != null)
                                    {
                                        cleanArtist    = parsed.Value.Artist;
                                        dbTrack.Title  = parsed.Value.Title;
                                    }
                                }

                                if (!string.IsNullOrWhiteSpace(cleanArtist))
                                    dbTrack.Artist = cleanArtist;
                            }

                            if (!string.IsNullOrEmpty(dbTrack.Title))
                            {
                                var parsed = NullWave.Services.Metadata.TrackTitleParser
                                    .TryParseArtistTitle(dbTrack.Title);

                                if (parsed != null &&
                                    (dbTrack.Artist.Equals("Unknown Artist", StringComparison.OrdinalIgnoreCase) ||
                                    string.IsNullOrWhiteSpace(dbTrack.Artist)))
                                {
                                    dbTrack.Artist = parsed.Value.Artist;
                                    dbTrack.Title  = parsed.Value.Title;
                                }
                            }

                            dbTrack.AlbumArtPath = await _albumArtService.GetArtPathAsync(dbTrack);
                            _libraryService.Update(dbTrack);
                            Log.Debug("[DownloadService] Track ready: '{Title}' by '{Artist}' → {Path}",
                                dbTrack.Title, dbTrack.Artist, finalFilePath);
                            onTrackReady?.Invoke(dbTrack);
                        }
                    }

                    PlaylistBatchProgress?.Invoke(completedCount, tracks.Count, skippedCount);

                    if (i < tracks.Count - 1)
                    {
                        var delayMs = GetThrottleDelayMs();
                        Log.Debug("Throttling download to avoid rate limits... sleeping for {Delay}ms", delayMs);
                        await Task.Delay(delayMs, ct);
                    }
                }
                catch (OperationCanceledException)
                {
                    Log.Warning("Playlist download cancelled by user.");
                    DownloadCompleted -= OnCompleted;
                    DownloadFailed    -= OnFailed;
                    break;
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Skipping unavailable track {Url}: {Msg}", cleanUrl, ex.Message);
                    _libraryService.Remove(trackId);
                    failedCount++;
                    DownloadCompleted -= OnCompleted;
                    DownloadFailed    -= OnFailed;
                    PlaylistBatchProgress?.Invoke(completedCount, tracks.Count, skippedCount);
                    continue;
                }
            }

            Log.Information("Playlist download complete");
            PlaylistBatchCompleted?.Invoke(completedCount, failedCount, skippedCount);
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

    private string? FindMostRecentUnlinkedDownload()
    {
        var dir = new DirectoryInfo(_downloadDir);
        if (!dir.Exists) return null;

        var linkedPaths = _libraryService.GetAll()
            .Where(t => !string.IsNullOrEmpty(t.FilePath))
            .Select(t => Path.GetFullPath(t.FilePath!))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        FileInfo? newest = null;
        foreach (var file in dir.GetFiles())
        {
            if (!new[] { ".mp3", ".m4a", ".ogg", ".opus", ".wav", ".flac" }.Contains(file.Extension.ToLowerInvariant())) continue;
            if (linkedPaths.Contains(file.FullName)) continue;
            if (newest == null || file.LastWriteTime > newest.LastWriteTime) newest = file;
        }
        return newest?.FullName;
    }

    public string DownloadDirectory => _downloadDir;
}