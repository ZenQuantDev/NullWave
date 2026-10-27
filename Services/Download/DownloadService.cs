using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
        if (existing != null)
        {
            if (existing.Title == "Track" && title != "Track")
            {
                existing.Title = title;
                existing.Artist = artist;
            }
            return existing;
        }

        var job = new DownloadJob
        {
            TrackId = trackId,
            Url = url,
            Title = title,
            Artist = artist,
            RetryAction = () => _ = DownloadAsync(trackId, url)
        };
        
        void AddJob() => ActiveJobs.Insert(0, job);
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) AddJob();
        else
        {
            try { AddJob(); }
            catch { Avalonia.Threading.Dispatcher.UIThread.Post(AddJob); }
        }
        return job;
    }

    public void UpdateJobMetadata(string trackId, string? title, string? artist)
    {
        void Apply()
        {
            var job = ActiveJobs.FirstOrDefault(j => j.TrackId == trackId);
            if (job == null) return;
            if (!string.IsNullOrWhiteSpace(title)) job.Title = title;
            if (!string.IsNullOrWhiteSpace(artist)) job.Artist = artist;
        }

        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) Apply();
        else
        {
            try { Apply(); }
            catch { Avalonia.Threading.Dispatcher.UIThread.Post(Apply); }
        }
    }

    private void PruneCompletedJobs()
    {
        void Prune()
        {
            while (ActiveJobs.Count > 25)
            {
                var victim = ActiveJobs.LastOrDefault(j => j.IsCompleted || j.IsFailed) ?? ActiveJobs[^1];
                if (victim != null) ActiveJobs.Remove(victim);
            }
        }

        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) Prune();
        else
        {
            try { Prune(); }
            catch { Avalonia.Threading.Dispatcher.UIThread.Post(Prune); }
        }
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
    
    // NEW: Regex to strip list= parameter from URLs
    private static readonly Regex ListParamRegex = new(@"[?&]list=[^&]*", RegexOptions.Compiled);

    private readonly HashSet<string> _activeDownloads = new();
    private int _activeDownloadCount; 
    private CancellationTokenSource? _currentDownloadCts;
    private SemaphoreSlim _semaphore = new(2, 5);
    private int _currentLimit = 2;
    private static bool? _aria2cAvailable;
    private static readonly object _aria2cLock = new();
    private static bool? _ffmpegAvailable;
    private static readonly object _ffmpegLock = new();

    private volatile int _backoffMultiplier = 1;
    private volatile bool _rateLimitTriggered = false;
    private static string? _cachedJsRuntime;
    private static bool _jsRuntimeProbed;

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

    // --- Unified ProcessRunner Detection ---
    private static async Task<bool> IsAria2cAvailableAsync()
    {
        if (_aria2cAvailable == true) return true;
        
        var exists = await ProcessRunner.CheckExistsAsync("aria2c", new[] { "--version" }, 2000);
        if (exists)
        {
            lock (_aria2cLock) { _aria2cAvailable = true; }
            Log.Information("[DownloadService] aria2c detected - enabling multi-connection downloads");
            return true;
        }

        Log.Information("[DownloadService] aria2c not found on PATH - using yt-dlp's native downloader");
        return false;
    }

    private static async Task<bool> IsFfmpegAvailableAsync()
    {
        if (_ffmpegAvailable == true) return true;
        
        var exists = await ProcessRunner.CheckExistsAsync("ffmpeg", new[] { "-version" }, 2000);
        if (exists)
        {
            lock (_ffmpegLock) { _ffmpegAvailable = true; }
            Log.Information("[DownloadService] ffmpeg detected - enabling metadata and thumbnail embedding");
            return true;
        }

        Log.Information("[DownloadService] ffmpeg not found on PATH - downloading native audio streams without embedding metadata/thumbnails");
        return false;
    }

    // UPDATED: JS runtime detection - removed Bun (deprecated), added QuickJS
    private static string? GetJsRuntimeArg()
    {
        if (_jsRuntimeProbed) return _cachedJsRuntime;

        static bool OnPath(string name)
        {
            var exe = OperatingSystem.IsWindows() ? name + ".exe" : name;
            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                try { if (File.Exists(Path.Combine(dir.Trim(), exe))) return true; } catch { }
            }
            return false;
        }

        string? result = null;
        bool found = false;
        // Order: Deno (preferred) → Node → QuickJS
        if (OnPath("deno")) { result = null; found = true; }      // yt-dlp's default
        else if (OnPath("node")) { result = "node"; found = true; }
        else if (OnPath("qjs") || OnPath("quickjs")) { result = "qjs"; found = true; }

        if (found)
        {
            _cachedJsRuntime = result;
            _jsRuntimeProbed = true;
        }
        return result;
    }

    // UPDATED: Removed Bun from detection
    private static bool HasAnyJsRuntime()
    {
        static bool OnPath(string name)
        {
            var exe = OperatingSystem.IsWindows() ? name + ".exe" : name;
            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                try { if (File.Exists(Path.Combine(dir.Trim(), exe))) return true; } catch { }
            }
            return false;
        }
        return OnPath("deno") || OnPath("node") || OnPath("qjs") || OnPath("quickjs");
    }

    private void AppendSpeedAndAuthArgs(List<string> args, bool useAria2c)
    {
        var jsRuntime = GetJsRuntimeArg();
        if (jsRuntime is { } runtime)
        {
            args.Add("--js-runtimes");
            args.Add(runtime);
        }
        if (useAria2c)
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

    private bool CheckConsent()
    {
        var consent = _prefsService.Current.DownloaderConsent;
        if (consent == null || !consent.IsCurrent)
        {
            Log.Warning("[DownloadService] Download blocked: consent not given or outdated");
            ToastService.Instance.Show(
                "Please accept the downloader terms in Settings → Downloads", 
                ToastType.Warning, 
                scope: "download");
            return false;
        }
        return true;
    }
    
    public async Task DownloadAsync(
        string trackId,
        string url,
        string audioFormat = "best",
        string audioQuality = "best",
        bool allowPlaylist = false,
        bool isInteractive = true,
        string? title = null,
        string? artist = null,
        CancellationToken ct = default)
    {
        // CRITICAL FIX: Check consent FIRST
        if (!CheckConsent())
        {
            DownloadFailed?.Invoke(trackId, "Download terms not accepted", isInteractive);
            return;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uriResult) || 
            (uriResult.Scheme != Uri.UriSchemeHttp && uriResult.Scheme != Uri.UriSchemeHttps))
        {
            Log.Warning("[DownloadService] Blocked invalid or non-HTTP URL: {Url}", url);
            DownloadFailed?.Invoke(trackId, "Invalid URL format", isInteractive);
            return;
        }

        if (url.Contains("youtu.be") || url.Contains("youtube.com"))
        {
            url = SiParamRegex1.Replace(url, "");
            url = SiParamRegex2.Replace(url, "?");
            url = SiParamRegex3.Replace(url, "");
        }

        // FIXED: Strip list= parameter instead of rejecting
        if (!allowPlaylist && url.Contains("list="))
        {
            url = ListParamRegex.Replace(url, "");
            // Clean up any trailing ? or & after removal
            url = url.TrimEnd('?', '&');
            Log.Information("[DownloadService] Stripped list= parameter from URL: {Url}", url);
        }

        if (!allowPlaylist && url.Contains("playlist?"))
        {
            Log.Warning("[DownloadService] Blocked playlist URL in single-track pipeline: {Url}", url);
            DownloadFailed?.Invoke(trackId, "Playlist URLs are not supported in single-track mode", isInteractive);
            return;
        }

        var safeDownloadDir = Path.GetFullPath(_downloadDir);
        var outputTemplate = Path.Combine(safeDownloadDir, "%(title).150B [%(id)s].%(ext)s");
        
        var qualityValue = audioQuality switch
        {
            "best" => "0", "320"  => "0", "192"  => "2", "128"  => "4", "96"   => "6", _ => "0"
        };

        var useFfmpeg = await IsFfmpegAvailableAsync();

        var formatSelector = ChooseFormatSelector(useFfmpeg, audioFormat, OperatingSystem.IsWindows());

        var args = new List<string>
        {
            url, "-f", formatSelector, "--output", outputTemplate, "--print", "after_move:filepath",
            "--ignore-errors",
            "--parse-metadata", "uploader:%(artist)s", "--parse-metadata", "channel:%(artist)s"
        };
        if (useFfmpeg)
        {
            args.Add("--embed-metadata");
            
            if (!string.Equals(audioFormat, "best", StringComparison.OrdinalIgnoreCase))
            {
                args.Add("--embed-thumbnail");
                args.Add("--extract-audio");
                args.Add("--audio-format");
                args.Add(audioFormat);
                args.Add("--audio-quality");
                args.Add(qualityValue);
            }
        }
        else
        {
            Log.Warning("[DownloadService] ffmpeg missing: skipping metadata/thumbnail embedding and audio transcoding.");
        }

        var useAria2c = _prefsService.Current.UseAria2c && await IsAria2cAvailableAsync();
        AppendSpeedAndAuthArgs(args, useAria2c);

        if (!allowPlaylist) args.Add("--no-playlist");
        else args.Add("--yes-playlist");

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

        cts.CancelAfter(TimeSpan.FromMinutes(20));
        ct = cts.Token;

        Log.Debug("Starting download: {Url} (format={Format}, quality={Quality})", url, audioFormat, audioQuality);

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

            string? outputFilePath = null;

            var recentStdErr = new List<string>();
            var result = await ProcessRunner.RunWithStreamingAsync(
                "yt-dlp", args,
                onStdOutLine: line =>
                {
                    if (string.IsNullOrWhiteSpace(line)) return;
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
                },
                onStdErrLine: line =>
                {
                    if (!string.IsNullOrEmpty(line))
                    {
                        Log.Debug("yt-dlp stderr: {Line}", line);
                        if (recentStdErr.Count >= 20) recentStdErr.RemoveAt(0);
                        recentStdErr.Add(line);

                        // FIXED: More precise 429 detection
                        if (line.Contains("HTTP Error 429") || 
                            line.Contains("Too Many Requests", StringComparison.OrdinalIgnoreCase))
                        {
                            lock (_aria2cLock) { _rateLimitTriggered = true; }
                        }
                    }
                },
                timeout: TimeSpan.FromMinutes(20),
                ct: ct,
                resolveViaPath: true);

            if (result.Canceled || result.TimedOut)
            {
                Log.Warning("Download cancelled or timed out: {TrackId}", trackId);
                DownloadFailed?.Invoke(trackId, result.TimedOut ? "Timed out" : "Cancelled", isInteractive);
                job.IsFailed = true;
                job.Status = "Failed";
                job.ErrorMessage = result.TimedOut ? "Timed out" : "Cancelled";
                PruneCompletedJobs();
                return;
            }

            if (result.ExitCode == 0 && outputFilePath != null && File.Exists(outputFilePath))
            {
                Log.Debug("Download complete: {Path}", outputFilePath);
                job.IsCompleted = true;
                job.Status = "Completed";
                job.Progress = 100;
                DownloadCompleted?.Invoke(trackId, outputFilePath, isInteractive);
                PruneCompletedJobs();
            }
            else if (result.ExitCode == 0)
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
                
                var errText = string.Join("\n", recentStdErr);
                string friendlyError = $"yt-dlp exited with code {result.ExitCode}";

                // UPDATED: Error message now mentions QuickJS instead of Bun
                if ((errText.Contains("n challenge", StringComparison.OrdinalIgnoreCase) || 
                    errText.Contains("Requested format is not available", StringComparison.OrdinalIgnoreCase)) 
                    && !HasAnyJsRuntime())
                {
                    friendlyError = "YouTube signature error: Install Deno, Node.js, or QuickJS.";
                }
                else if (errText.Contains("not a bot", StringComparison.OrdinalIgnoreCase))
                {
                    friendlyError = "YouTube rate limit: Try using browser cookies in Settings.";
                }
                else if (errText.Contains("HTTP Error 429", StringComparison.OrdinalIgnoreCase))
                {
                    friendlyError = "YouTube rate limit (429): Wait a few minutes or use browser cookies.";
                }

                job.ErrorMessage = friendlyError;
                DownloadFailed?.Invoke(trackId, friendlyError, isInteractive);
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
        // CRITICAL FIX: Check consent FIRST
        if (!CheckConsent())
        {
            ToastService.Instance.Show(
                "Please accept the downloader terms in Settings → Downloads", 
                ToastType.Warning, 
                scope: "download");
            return;
        }

        Log.Information("Starting playlist download: {Url}", playlistUrl);
        try
        {
            var metadataResult = await ProcessRunner.RunAsync(
                "yt-dlp",
                new List<string> { "--flat-playlist", "--dump-json", "--ignore-errors", "--no-download", playlistUrl },
                timeout: TimeSpan.FromMinutes(5),
                ct: ct,
                resolveViaPath: true);

            if (metadataResult.ExitCode != 0 || metadataResult.TimedOut || metadataResult.Canceled)
            {
                Log.Error("Failed to fetch playlist metadata");
                return;
            }

            var metadataOutput = metadataResult.StandardOutput;
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
                    if (root.TryGetProperty("artist", out var artistProp) && !string.IsNullOrWhiteSpace(artistProp.GetString()))
                        artist = artistProp.GetString()!.Trim();
                    else if (root.TryGetProperty("creator", out var creatorProp) && !string.IsNullOrWhiteSpace(creatorProp.GetString()))
                        artist = creatorProp.GetString()!.Trim();
                    else if (root.TryGetProperty("uploader", out var uploaderProp) && !string.IsNullOrWhiteSpace(uploaderProp.GetString()))
                        artist = uploaderProp.GetString()!.Trim();
                    else if (root.TryGetProperty("channel", out var channelProp) && !string.IsNullOrWhiteSpace(channelProp.GetString()))
                        artist = channelProp.GetString()!.Trim();

                    if (_prefsService.Current.AutoCleanMetadata)
                        artist = TopicSuffixRegex.Replace(artist, string.Empty).Trim();

                    if (string.IsNullOrWhiteSpace(artist) || artist.Equals("Unknown Artist", StringComparison.OrdinalIgnoreCase) || artist.Equals("Various Artists", StringComparison.OrdinalIgnoreCase))
                    {
                        if (root.TryGetProperty("channel", out var fallbackChannelProp) && !string.IsNullOrWhiteSpace(fallbackChannelProp.GetString()))
                        {
                            var channelName = fallbackChannelProp.GetString()!.Trim();
                            if (!channelName.Equals("Various Artists", StringComparison.OrdinalIgnoreCase) && !channelName.EndsWith("- Topic", StringComparison.OrdinalIgnoreCase))
                                artist = channelName;
                        }
                    }

                    string cleanTitle = rawTitle;
                    if (string.IsNullOrWhiteSpace(artist) || artist.Equals("Unknown Artist", StringComparison.OrdinalIgnoreCase))
                    {
                        var parsed = NullWave.Helpers.TrackNormalizer.TrySplit(rawTitle);
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

                    await DownloadAsync(trackId.ToString(), cleanUrl, _prefsService.Current.AudioFormat, _prefsService.Current.AudioQuality, 
                                        allowPlaylist: false, isInteractive: false, 
                                        title: title, artist: artist, ct: ct);

                    tcs.TrySetResult(false);

                    DownloadCompleted -= OnCompleted;
                    DownloadFailed    -= OnFailed;

                    var success = await tcs.Task;

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
                                    dbTrack.Artist, @"\s*-\s*Topic\s*$", string.Empty, System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();

                                if (string.IsNullOrWhiteSpace(cleanArtist) || cleanArtist.Equals("Unknown Artist", StringComparison.OrdinalIgnoreCase) || cleanArtist.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                                {
                                    var parsed = NullWave.Helpers.TrackNormalizer.TrySplit(dbTrack.Title);
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
                                var parsed = NullWave.Helpers.TrackNormalizer.TrySplit(dbTrack.Title);
                                if (parsed != null && NullWave.Helpers.TrackNormalizer.IsPlaceholderArtist(dbTrack.Artist))
                                {
                                    dbTrack.Artist = parsed.Value.Artist;
                                    dbTrack.Title  = parsed.Value.Title;
                                }
                            }

                            dbTrack.AlbumArtPath = await _albumArtService.GetArtPathAsync(dbTrack);
                            
                            if (!string.IsNullOrEmpty(dbTrack.AlbumArtPath) && File.Exists(dbTrack.AlbumArtPath))
                                ThumbnailCropper.TrimLetterboxInPlace(dbTrack.AlbumArtPath);
                            
                            _libraryService.Update(dbTrack);

                            Log.Debug("[DownloadService] Track ready: '{Title}' by '{Artist}' → {Path}", dbTrack.Title, dbTrack.Artist, finalFilePath);
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
                        Log.Debug("Throttling download to avoid rate limits... sleeping for {Delay}ms (Multiplier: {Mult}x)", delayMs, backoffMultiplier);
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
            if (newest == null || file.LastWriteTimeUtc > newest.LastWriteTimeUtc) newest = file;
        }

        if (newest == null) return null;

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

        if (Volatile.Read(ref _activeDownloadCount) <= 1)
            return newest.FullName;

        Log.Warning("[DownloadService] filepath not captured and multiple downloads in flight; refusing ambiguous fallback");
        return null;
    }

    private static void PostToUiThread(Action action)
    {
        try
        {
            if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
                action();
            else
                Avalonia.Threading.Dispatcher.UIThread.Post(action);
        }
        catch { }
    }

    internal static string ChooseFormatSelector(bool useFfmpeg, string audioFormat, bool isWindows)
    {
        var nativeContainer = !useFfmpeg || string.Equals(audioFormat, "best", StringComparison.OrdinalIgnoreCase);
        return nativeContainer && isWindows ? "bestaudio[ext=m4a]/bestaudio/best" : "bestaudio/best";
    }

    public string DownloadDirectory => _downloadDir;
}