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

    public ObservableCollection<DownloadJob> ActiveJobs { get; } = new();

    private DownloadJob GetOrCreateJob(string trackId, string url, string title, string artist)
    {
        var existing = ActiveJobs.FirstOrDefault(j => j.Url == url && !j.IsCompleted && !j.IsFailed);
        if (existing != null) return existing;

        var job = new DownloadJob
        {
            Url = url,
            Title = title,
            Artist = artist,
            RetryAction = () => _ = DownloadAsync(trackId, url)
        };
        Avalonia.Threading.Dispatcher.UIThread.Post(() => ActiveJobs.Insert(0, job));
        return job;
    }

    private void PruneCompletedJobs()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            while (ActiveJobs.Count > 25)
            {
                var victim = ActiveJobs.LastOrDefault(j => j.IsCompleted || j.IsFailed) ?? ActiveJobs[^1];
                if (victim != null) ActiveJobs.Remove(victim);
            }
        });
    }

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

    private static readonly Regex SiParamRegex1 = new(@"&si=[^&]*", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SiParamRegex2 = new(@"\?si=[^&]*&", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SiParamRegex3 = new(@"\?si=[^&]*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly HashSet<string> _activeDownloads = new();
    private int _activeDownloadCount; // NEW: Tracks in-flight downloads for fallback safety
    private CancellationTokenSource? _currentDownloadCts;
    private SemaphoreSlim _semaphore = new(2, 5);
    private int _currentLimit = 2;
    private static bool? _aria2cAvailable;
    private static readonly object _aria2cLock = new();

    // Dynamic Throttling State
    private volatile int _backoffMultiplier = 1;
    private volatile bool _rateLimitTriggered = false;

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
                Log.Information("[DownloadService] aria2c not found on PATH - using yt-dlp's native downloader");
            else
                Log.Information("[DownloadService] aria2c detected - enabling multi-connection downloads");

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
        string? title = null,
        string? artist = null,
        CancellationToken ct = default)
    {
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

        // FIX: append the video id so two tracks sharing a title (two songs called "Intro")
        // never collide and overwrite each other on disk.
        var outputTemplate = Path.Combine(_downloadDir, "%(title).150B [%(id)s].%(ext)s");
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
            args.Add("--no-playlist");
        else
            args.Add("--yes-playlist");

        lock (_activeDownloads)
        {
            if (_activeDownloads.Contains(url))
            {
                Log.Debug("[DownloadService] Skipping duplicate download for {Url}", url);
                // FIX: fire the failure event instead of a silent return, so any caller
                // awaiting a TaskCompletionSource tied to this trackId doesn't hang forever.
                DownloadFailed?.Invoke(trackId, "Already downloading", isInteractive);
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

        // FIX: a hung yt-dlp process (network stall, waiting on input) used to hold a
        // concurrency slot forever. Give every download a hard ceiling.
        cts.CancelAfter(TimeSpan.FromMinutes(20));
        ct = cts.Token;

        Log.Debug("Starting download: {Url} (format={Format}, quality={Quality})",
            url, audioFormat, audioQuality);

        // FIX: everything from the semaphore acquire through cleanup is now ONE try/finally.
        // Previously, if the token cancelled while still queued (before WaitAsync returned),
        // the method exited before ever reaching the cleanup that removed `url` from
        // `_activeDownloads` — leaking it until restart. We also capture the exact semaphore
        // instance we acquired from and release *that* instance, since `UpdateConcurrencyLimit`
        // can swap `_semaphore` to a new object mid-download; releasing whatever the field
        // currently points to could throw or corrupt the new semaphore's count.
        SemaphoreSlim? acquiredSemaphore = null;
        var activeDownloadCounted = false;
        DownloadJob? job = null;

        try
        {
            var sem = _semaphore;
            try
            {
                await sem.WaitAsync(ct);
            }
            catch (ObjectDisposedException)
            {
                Log.Debug("[DownloadService] Semaphore rebuilt mid-wait, re-acquiring");
                sem = _semaphore;
                await sem.WaitAsync(ct);
            }
            acquiredSemaphore = sem;

            Interlocked.Increment(ref _activeDownloadCount);
            activeDownloadCounted = true;

            job = GetOrCreateJob(trackId, url, title ?? "Track", artist ?? "Unknown");
            job.Status = "Downloading...";
            job.IsIndeterminate = false;

            var psi = new ProcessStartInfo(PlatformHelper.ResolveExecutable("yt-dlp"))
            {
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true
            };

            foreach (var arg in args)
                psi.ArgumentList.Add(arg);

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
                    job.Progress = pct;
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
                {
                    Log.Debug("yt-dlp stderr: {Line}", e.Data);
                    if (e.Data.Contains("429") || e.Data.Contains("Too Many Requests", StringComparison.OrdinalIgnoreCase))
                    {
                        lock (_aria2cLock) { _rateLimitTriggered = true; }
                    }
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            try
            {
                await process.WaitForExitAsync(ct);
                process.WaitForExit();
            }
            catch (OperationCanceledException)
            {
                // yt-dlp spawns ffmpeg as a child process, so kill the entire tree.
                if (!process.HasExited)
                {
                    try { process.Kill(true); } catch { /* ignore teardown errors */ }
                }
                Log.Warning("Download cancelled or timed out: {TrackId}", trackId);
                DownloadFailed?.Invoke(trackId, "Cancelled", isInteractive);
                job.IsFailed = true;
                job.Status = "Failed";
                job.ErrorMessage = "Cancelled";
                PruneCompletedJobs();
                return;
            }

            if (process.ExitCode == 0 && outputFilePath != null && File.Exists(outputFilePath))
            {
                Log.Debug("Download complete: {Path}", outputFilePath);
                job.IsCompleted = true;
                job.Status = "Completed";
                job.Progress = 100;
                DownloadCompleted?.Invoke(trackId, outputFilePath, isInteractive);
                PruneCompletedJobs();
            }
            else if (process.ExitCode == 0)
            {
                var recent = FindMostRecentUnlinkedDownload(title);
                if (recent != null)
                {
                    Log.Warning("[DownloadService] filepath not captured, using most recent: {Path}", recent);
                    job.IsCompleted = true;
                    job.Status = "Completed";
                    job.Progress = 100;
                    DownloadCompleted?.Invoke(trackId, recent, isInteractive);
                    PruneCompletedJobs();
                }
                else
                {
                    Log.Error("[DownloadService] Download exited 0 but no output file found for {TrackId}", trackId);
                    DownloadFailed?.Invoke(trackId, "File not found after download", isInteractive);
                    job.IsFailed = true;
                    job.Status = "Failed";
                    job.ErrorMessage = "File not found after download";
                    PruneCompletedJobs();
                }
            }
            else
            {
                job.IsFailed = true;
                job.Status = "Failed";
                job.ErrorMessage = $"Exit code {process.ExitCode}";
                DownloadFailed?.Invoke(trackId, $"yt-dlp exited with code {process.ExitCode}", isInteractive);
                PruneCompletedJobs();
            }
        }
        catch (OperationCanceledException)
        {
            Log.Warning("Download cancelled while queued: {TrackId}", trackId);
            DownloadFailed?.Invoke(trackId, "Cancelled", isInteractive);
            if (job != null) { job.IsFailed = true; job.Status = "Failed"; job.ErrorMessage = "Cancelled"; }
            PruneCompletedJobs();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Download exception for {Url}", url);
            DownloadFailed?.Invoke(trackId, ex.Message, isInteractive);
            PruneCompletedJobs();
        }
        finally
        {
            if (activeDownloadCounted) Interlocked.Decrement(ref _activeDownloadCount);
            acquiredSemaphore?.Release();
            lock (_activeDownloads)
                _activeDownloads.Remove(url);
        }
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
            // FIX: Use ArgumentList to prevent argument injection
            var metadataPsi = new ProcessStartInfo(PlatformHelper.ResolveExecutable("yt-dlp"))
            {
                ArgumentList =
                {
                    "--flat-playlist",
                    "--dump-json",
                    "--ignore-errors",
                    "--no-download",
                    "--js-runtimes", "node",
                    "--remote-components", "ejs:github",
                    playlistUrl
                },
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

                    var rawTitle = root.TryGetProperty("title", out var titleProp)
                        ? titleProp.GetString() ?? "Unknown Track" : "Unknown Track";

                    string videoId = root.TryGetProperty("id", out var idProp)
                        ? (idProp.GetString() ?? "") : "";

                    if (string.IsNullOrEmpty(videoId) && root.TryGetProperty("url", out var urlProp))
                    {
                        var rawUrl = urlProp.GetString() ?? "";
                        var vMatch = Regex.Match(rawUrl, @"[?&]v=([^&]+)");
                        if (vMatch.Success) videoId = vMatch.Groups[1].Value;
                        else if (rawUrl.Contains("youtu.be/")) videoId = rawUrl.Split("youtu.be/")[1].Split('?')[0];
                    }

                    if (string.IsNullOrEmpty(videoId))
                    {
                        Log.Warning("[DownloadService] Skipping playlist track with missing video ID: {Title}", rawTitle);
                        continue;
                    }

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

                    // FIX: Fallback to channel name if artist is generic/unknown
                    if (string.IsNullOrWhiteSpace(artist) ||
                        artist.Equals("Unknown Artist", StringComparison.OrdinalIgnoreCase) ||
                        artist.Equals("Various Artists", StringComparison.OrdinalIgnoreCase))
                    {
                        if (root.TryGetProperty("channel", out var fallbackChannelProp) &&
                            !string.IsNullOrWhiteSpace(fallbackChannelProp.GetString()))
                        {
                            var channelName = fallbackChannelProp.GetString()!.Trim();
                            if (!channelName.Equals("Various Artists", StringComparison.OrdinalIgnoreCase) &&
                                !channelName.EndsWith("- Topic", StringComparison.OrdinalIgnoreCase))
                            {
                                artist = channelName;
                            }
                        }
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

                    // FIX: Pass real title/artist from playlist enumeration to DownloadAsync
                    await DownloadAsync(trackId.ToString(), cleanUrl, audioFormat: "mp3", audioQuality: "best", 
                                        allowPlaylist: false, isInteractive: false, 
                                        title: title, artist: artist, ct: ct);

                    // FIX: DownloadAsync can return without ever firing an event (e.g. the
                    // duplicate-URL guard), which used to leave `tcs` unresolved and hang this
                    // loop forever. No-op if an event already completed it.
                    tcs.TrySetResult(false);

                    DownloadCompleted -= OnCompleted;
                    DownloadFailed    -= OnFailed;

                    var success = await tcs.Task;

                    // Apply Exponential Backoff Logic
                    lock (_aria2cLock)
                    {
                        if (_rateLimitTriggered)
                        {
                            _backoffMultiplier = Math.Min(_backoffMultiplier * 2, 8);
                            _rateLimitTriggered = false;
                            Log.Warning("[DownloadService] Rate limit detected. Increasing backoff multiplier to {Mult}x.", _backoffMultiplier);
                        }
                        else if (_backoffMultiplier > 1)
                        {
                            _backoffMultiplier = Math.Max(1, _backoffMultiplier / 2);
                        }
                    }

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
                            
                            // FIX: Square-crop the freshly cached thumbnail to eliminate YouTube's
                            // baked-in 4:3 letterbox bars. Idempotent: already-square files are
                            // left untouched by ThumbnailCropper.
                            if (!string.IsNullOrEmpty(dbTrack.AlbumArtPath) && File.Exists(dbTrack.AlbumArtPath))
                            {
                                ThumbnailCropper.CropFileToSquare(dbTrack.AlbumArtPath);
                            }
                            
                            _libraryService.Update(dbTrack);

                            Log.Debug("[DownloadService] Track ready: '{Title}' by '{Artist}' → {Path}",
                                dbTrack.Title, dbTrack.Artist, finalFilePath);
                            onTrackReady?.Invoke(dbTrack);
                        }
                    }

                    PlaylistBatchProgress?.Invoke(completedCount, tracks.Count, skippedCount);

                    if (i < tracks.Count - 1)
                    {
                        var baseDelay = GetThrottleDelayMs();
                        int backoffMultiplier;
                        lock (_aria2cLock) backoffMultiplier = _backoffMultiplier;
                        var delayMs = baseDelay * backoffMultiplier;
                        Log.Debug("Throttling download to avoid rate limits... sleeping for {Delay}ms (Multiplier: {Mult}x)", delayMs, _backoffMultiplier);
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

    /// <summary>
    /// Fallback for when yt-dlp exits 0 but the --print filepath line never arrived.
    /// Prefers a file whose name matches the expected title; only falls back to
    /// "newest unlinked file" when a single download is in flight, so two concurrent
    /// playlist downloads can never mis-attribute each other's files.
    /// </summary>
    private string? FindMostRecentUnlinkedDownload(string? expectedTitle = null)
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

        if (newest == null) return null;

        // 1) Exact-ish title match wins (yt-dlp sanitizes quotes/punctuation, so compare alnum-only)
        if (!string.IsNullOrWhiteSpace(expectedTitle))
        {
            static string Norm(string s) => new string(s.Where(char.IsLetterOrDigit).ToArray());
            var want = Norm(expectedTitle);
            var match = dir.GetFiles()
                .Where(f => !linkedPaths.Contains(f.FullName) &&
                            new[] { ".mp3", ".m4a", ".ogg", ".opus", ".wav", ".flac" }.Contains(f.Extension.ToLowerInvariant()))
                .FirstOrDefault(f => Norm(Path.GetFileNameWithoutExtension(f.Name)) == want);
            if (match != null) return match.FullName;
        }

        // 2) "Newest unlinked" only when nothing else is downloading concurrently
        if (Volatile.Read(ref _activeDownloadCount) <= 1)
            return newest.FullName;

        Log.Warning("[DownloadService] filepath not captured and multiple downloads in flight; refusing ambiguous fallback");
        return null;
    }

    /// <summary>
    /// Safely posts to the UI thread. Swallows exceptions in test/headless environments 
    /// where the Avalonia dispatcher hasn't been initialized.
    /// </summary>
    private static void PostToUiThread(Action action)
    {
        try
        {
            if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
                action();
            else
                Avalonia.Threading.Dispatcher.UIThread.Post(action);
        }
        catch 
        { 
            // Ignored: Test environment or headless execution where dispatcher is unavailable 
        }
    }

    public string DownloadDirectory => _downloadDir;
}