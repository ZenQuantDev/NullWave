using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
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
            RetryAction = () => _ = DownloadAsync(trackId, url,
                _prefsService.Current.AudioFormat, _prefsService.Current.AudioQuality,
                title: title, artist: artist)
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
    
    private static readonly Regex ListParamRegex = new(@"[?&]list=[^&]*", RegexOptions.Compiled);
    private static readonly Regex YtIdRegex = new(@"(?:[?&]v=|youtu\.be/)([A-Za-z0-9_-]{11})", RegexOptions.Compiled);
    private static readonly string[] AudioExts = { ".mp3", ".m4a", ".ogg", ".opus", ".wav", ".flac" };

    private readonly HashSet<string> _activeDownloads = new();
    private readonly HashSet<string> _activePlaylists = new(); 
    private static readonly Regex ListIdRegex = new(@"[?&]list=([^&]+)", RegexOptions.Compiled);
    private int _activeDownloadCount; 
    private CancellationTokenSource? _currentDownloadCts;
    
    // Never replace or dispose the semaphore
    private readonly SemaphoreSlim _gate = new(2, 5);
    private int _currentLimit = 2;
    private int _shrinkDebt;
    private readonly object _limitLock = new();
    
    private static bool? _aria2cAvailable;
    private static readonly object _aria2cLock = new();
    private static bool? _ffmpegAvailable;
    private static readonly object _ffmpegLock = new();

    private volatile int _backoffMultiplier = 1;
    private volatile bool _rateLimitTriggered = false;
    private static string? _cachedJsRuntime;
    private static bool _jsRuntimeProbed;

    // Cookie fallback state
    private volatile bool _cookiesDisabledForSession;
    private int _cookieWarned;
    private readonly ConcurrentDictionary<string, DownloadFailureKind> _failureKinds = new();

    private bool CookiesActive =>
        !_cookiesDisabledForSession && !string.IsNullOrWhiteSpace(_prefsService.Current.YtDlpBrowserCookies);

    private void DisableCookiesForSession(string reason)
    {
        _cookiesDisabledForSession = true;
        if (Interlocked.Exchange(ref _cookieWarned, 1) != 0) return;
        
        Log.Warning("[DownloadService] Browser cookies unusable ({Reason}); continuing without them this session", reason);
        ToastService.Instance.Show(
            "Couldn't read your browser's cookies (close the browser, or clear the cookie setting). Downloading without them.",
            ToastType.Warning, scope: "download-cookies");
    }

    internal static string StripListParam(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return url;
        var q = System.Web.HttpUtility.ParseQueryString(u.Query);
        if (q["list"] == null) return url;
        q.Remove("list"); q.Remove("index"); q.Remove("start_radio");
        return new UriBuilder(u) { Query = q.ToString() }.Uri.AbsoluteUri;
    }

    public void UpdateConcurrencyLimit(int newLimit)
    {
        newLimit = Math.Clamp(newLimit, 1, 5);
        int release = 0, absorb = 0;
        lock (_limitLock)
        {
            var delta = newLimit - _currentLimit; 
            _currentLimit = newLimit;
            if (delta > 0) 
            { 
                var repay = Math.Min(delta, _shrinkDebt); 
                _shrinkDebt -= repay; 
                release = delta - repay; 
            }
            else 
            { 
                _shrinkDebt += -delta; 
                absorb = -delta; 
            }
        }
        
        if (release > 0) _gate.Release(release);
        
        for (var i = 0; i < absorb; i++)
        {
            _ = Task.Run(async () => 
            { 
                await _gate.WaitAsync(); 
                lock (_limitLock) 
                { 
                    if (_shrinkDebt > 0) 
                    { 
                        _shrinkDebt--; 
                        return; 
                    } 
                } 
                _gate.Release(); 
            });
        }

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

    private static string? ResolveOnPath(string name)
    {
        var exe = OperatingSystem.IsWindows() ? name + ".exe" : name;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try { var p = Path.Combine(dir.Trim(), exe); if (File.Exists(p)) return p; } catch { }
        }
        return null;
    }

    /// <summary>"major.minor.patch" from "v22.1.0" or "deno 2.3.1 (...)"; null if unreadable.</summary>
    internal static Version? ParseVersion(string? text)
    {
        var m = System.Text.RegularExpressions.Regex.Match(text ?? "", @"(\d+)\.(\d+)\.(\d+)");
        return m.Success ? new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value)) : null;
    }

    private static bool _jsRuntimeFound;
    private static long _negativeProbeTicks;

    private static async Task<bool> RuntimeOkAsync(string name, Version min)
    {
        var path = ResolveOnPath(name);
        if (path == null) return false;
        var r = await ProcessRunner.RunAsync(path, new[] { "--version" }, TimeSpan.FromSeconds(3), resolveViaPath: false);
        return r.ExitCode == 0 && ParseVersion(r.StandardOutput) is { } v && v >= min;
    }

    private static async Task<string?> GetJsRuntimeArgAsync()
    {
        if (_jsRuntimeProbed) return _cachedJsRuntime;
        await ProbeJsAsync();
        return _cachedJsRuntime;
    }

    private static async Task<bool> ProbeJsAsync()
    {
        if (_jsRuntimeProbed) 
        {
            if (!_jsRuntimeFound && Environment.TickCount64 - _negativeProbeTicks > 120_000) 
                _jsRuntimeProbed = false;
            else 
                return _jsRuntimeFound;
        }
        
        if (await RuntimeOkAsync("deno", new Version(2, 3, 0))) 
        { _cachedJsRuntime = null; _jsRuntimeFound = true; _jsRuntimeProbed = true; }
        else if (await RuntimeOkAsync("node", new Version(22, 0, 0))) 
        { _cachedJsRuntime = "node"; _jsRuntimeFound = true; _jsRuntimeProbed = true; }
        else 
        { 
            _jsRuntimeFound = false; _jsRuntimeProbed = true; 
            _negativeProbeTicks = Environment.TickCount64;
        }
        return _jsRuntimeFound;
    }

    private static bool HasAnyJsRuntime() => _jsRuntimeFound;

    private async Task AppendSpeedAndAuthArgsAsync(List<string> args, bool useAria2c)
    {
        YtDlpArgs.AppendNetworkArgs(args, _prefsService.Current);
        var jsRuntime = await GetJsRuntimeArgAsync();
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

        // FIXED: Use CookiesActive instead of raw preference check
        if (CookiesActive)
        {
            args.Add("--cookies-from-browser");
            args.Add(_prefsService.Current.YtDlpBrowserCookies);
        }
    }

    private int GetThrottleDelayMs()
    {
        // FIXED: Use CookiesActive instead of raw preference check
        return !CookiesActive
            ? Random.Shared.Next(3000, 8000)
            : Random.Shared.Next(600, 1800);
    }

    private bool HasConsent() => _prefsService.Current.DownloaderConsent is { IsCurrent: true };

    private bool CheckConsent()
    {
        if (HasConsent()) return true;

        Log.Warning("[DownloadService] Download blocked: consent not given or outdated");
        ToastService.Instance.Show(
            "Please accept the downloader terms in Settings → Downloads", 
            ToastType.Warning, 
            scope: "download");
        return false;
    }

    /// <summary>
    /// Interactive consent prompt. Returns true if consent is already accepted or user accepts now.
    /// Call this from UI code (TrackInputViewModel) before starting a download.
    /// </summary>
    public async Task<bool> EnsureConsentAsync()
    {
        if (HasConsent()) return true; // FIX: Use silent check to avoid double-toast

        var dialog = new Views.ConfirmDialog(
            "Enable Downloads?",
            "NullWave uses yt-dlp to download audio from YouTube, SoundCloud, and other platforms.\n\n" +
            "• You are responsible for respecting copyright and platform terms of service.\n" +
            "• NullWave does not host or provide any content.\n" +
            "• You can change this anytime in Settings → Downloads.\n\n" +
            "Enable downloading?");

        var accepted = await DialogService.Instance.ShowDialogAsync<bool>(dialog);

        if (accepted)
        {
            _prefsService.Update(p => p.DownloaderConsent = new DownloaderConsent
            {
                AcceptedVersion = DownloaderConsent.CurrentVersion,
                AcceptedAtUtc = DateTime.UtcNow
            });
            Log.Information("[DownloadService] User accepted downloader consent");
            return true;
        }

        Log.Information("[DownloadService] User declined downloader consent");
        ToastService.Instance.Show("Downloads remain disabled. Enable them in Settings → Downloads.", ToastType.Info, scope: "download");
        return false;
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

        if (!allowPlaylist && url.Contains("playlist?", StringComparison.OrdinalIgnoreCase))
        {
            Log.Warning("[DownloadService] Blocked playlist URL in single-track pipeline: {Url}", url);
            DownloadFailed?.Invoke(trackId, "Playlist URLs are not supported in single-track mode", isInteractive);
            return;
        }

        if (!allowPlaylist && url.Contains("list=", StringComparison.OrdinalIgnoreCase))
        {
            url = StripListParam(url);
            Log.Information("[DownloadService] Stripped list= parameter from URL: {Url}", url);
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
            "--ignore-errors"
        };

        var src = SourceDetector.Detect(url);
        var videoId = src == TrackSource.YouTube && YtIdRegex.Match(url) is { Success: true } idm ? idm.Groups[1].Value : null;

        if (src is TrackSource.YouTube or TrackSource.SoundCloud)
        {
            args.Add("--parse-metadata"); args.Add("uploader:%(artist)s");
            args.Add("--parse-metadata"); args.Add("channel:%(artist)s");
        }
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
        await AppendSpeedAndAuthArgsAsync(args, useAria2c);

        if (!allowPlaylist) args.Add("--no-playlist");
        else args.Add("--yes-playlist");

        if (!allowPlaylist && src != TrackSource.YouTube)
        {
            args.Add("--playlist-items");
            args.Add("1");
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
            cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            if (isInteractive) _currentDownloadCts = cts; // Stop button still targets the latest
        }

        cts.CancelAfter(TimeSpan.FromMinutes(20));
        ct = cts.Token;

        var activeDownloadCounted = false;
        DownloadJob? job = null;

        try
        {
            // FIXED: Use _gate directly, no more semaphore replacement
            await _gate.WaitAsync(ct);
            Interlocked.Increment(ref _activeDownloadCount);
            activeDownloadCounted = true;

            Log.Debug("Starting download: {Url} (format={Format}, quality={Quality})", url, audioFormat, audioQuality);

            job = GetOrCreateJob(trackId, url, title ?? "Track", artist ?? "Unknown");
            job.Status = "Downloading...";
            job.IsIndeterminate = false;

            string? outputFilePath = null;
            var recentStdErr = new List<string>();

            // FIXED: Wrap the run in a lambda so we can retry without cookies
            var run = () => ProcessRunner.RunWithStreamingAsync(
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

            var result = await run();

            // FIXED: Cookie retry logic
            if (result.ExitCode != 0 && !result.Canceled && !result.TimedOut
                && args.Contains("--cookies-from-browser")
                && DownloadFailureClassifier.Classify(string.Join("\n", recentStdErr), HasAnyJsRuntime())
                    == DownloadFailureKind.CookiesUnusable)
            {
                DisableCookiesForSession(recentStdErr.LastOrDefault() ?? "");
                var i = args.IndexOf("--cookies-from-browser");
                args.RemoveRange(i, 2);
                recentStdErr.Clear();
                outputFilePath = null;
                result = await run();
            }

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
                if (outputFilePath != null)
                    Log.Warning("[DownloadService] yt-dlp printed a path that doesn't exist: {Path}", outputFilePath);
                    
                var recent = FindMostRecentUnlinkedDownload(title, videoId);
                if (recent != null)
                {
                    Log.Information("[DownloadService] output located via fallback: {Path}", recent);
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
                
                // FIXED: Use classifier for better error messages
                var errText = string.Join("\n", recentStdErr);
                await ProbeJsAsync(); // Ensure we have the latest probe result for the classifier
                var kind = DownloadFailureClassifier.Classify(errText, HasAnyJsRuntime());
                var detail = recentStdErr.LastOrDefault(l => l.Contains("ERROR", StringComparison.OrdinalIgnoreCase))
                             ?? recentStdErr.LastOrDefault() ?? $"exit code {result.ExitCode}";
                
                var friendlyError = kind switch
                {
                    DownloadFailureKind.CookiesUnusable   => "Browser cookies couldn't be read. Close the browser or clear the cookie setting.",
                    DownloadFailureKind.BotCheck          => "YouTube wants a sign-in check. Try browser cookies in Settings.",
                    DownloadFailureKind.RateLimited       => "YouTube rate limit (429). Wait a few minutes.",
                    DownloadFailureKind.JsRuntimeMissing  => "YouTube signatures error. Install Deno 2.3+ or Node 22+.",
                    DownloadFailureKind.Unavailable       => "This video is unavailable.",
                    _                                     => $"yt-dlp failed: {detail}"
                };
                
                _failureKinds[trackId] = kind;
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
            if (activeDownloadCounted)
            {
                Interlocked.Decrement(ref _activeDownloadCount);
                _gate.Release(); // FIXED: Always release the gate
            }
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
        // Extract playlist ID for deduplication
        var m = ListIdRegex.Match(playlistUrl);
        var key = m.Success ? m.Groups[1].Value : playlistUrl;
        
        lock (_activePlaylists)
        {
            if (!_activePlaylists.Add(key))
            {
                Log.Information("[DownloadService] Playlist {Key} is already importing; ignoring", key);
                ToastService.Instance.Show("This playlist is already being imported.", ToastType.Info, scope: "download");
                return;
            }
        }
        
        try
        {
            await DownloadPlaylistCoreAsync(playlistUrl, onTrackReady, onTrackStarted, 
                onTrackCompleted, onTrackFailed, ct);
        }
        finally
        {
            lock (_activePlaylists) _activePlaylists.Remove(key);
        }
    }

    private async Task DownloadPlaylistCoreAsync(
        string playlistUrl,
        Action<Track>? onTrackReady = null,
        Action<string, int, int>? onTrackStarted = null,
        Action<string, string, string>? onTrackCompleted = null,
        Action<string, string>? onTrackFailed = null,
        CancellationToken ct = default)
    {
        if (!CheckConsent())
        {
            ToastService.Instance.Show(
                "Please accept the downloader terms in Settings → Downloads", 
                ToastType.Warning, 
                scope: "download");
            return;
        }

        // Detect YouTube auto-generated mixes (RD...) and cap at 50 tracks
        var listId = ListIdRegex.Match(playlistUrl).Groups[1].Value;
        var isMix = listId.StartsWith("RD", StringComparison.Ordinal) ||
                    playlistUrl.Contains("start_radio=1", StringComparison.OrdinalIgnoreCase);
        
        if (isMix)
        {
            Log.Warning("[DownloadService] Detected YouTube Mix (endless playlist), capping at 50 tracks: {Url}", playlistUrl);
            ToastService.Instance.Show(
                "YouTube Mix detected (endless playlist). Importing first 50 tracks only.",
                ToastType.Info,
                durationMs: 4000,
                scope: "download");
        }

        Log.Information("Starting playlist download: {Url}", playlistUrl);
        try
        {
            var metaArgs = new List<string> { "--flat-playlist", "--dump-json", "--ignore-errors", "--no-download" };
            if (isMix)
            {
                metaArgs.AddRange(new[] { "--playlist-end", "50" });
            }
            metaArgs.Add(playlistUrl);
            
            var metadataResult = await ProcessRunner.RunAsync(
                "yt-dlp",
                metaArgs,
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
            int envStreak = 0;

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
                        _failureKinds.TryRemove(trackId.ToString(), out var kind);
                        var environmental = kind is DownloadFailureKind.BotCheck 
                            or DownloadFailureKind.RateLimited
                            or DownloadFailureKind.JsRuntimeMissing 
                            or DownloadFailureKind.CookiesUnusable;
                        
                        if (!environmental)
                        {
                            _libraryService.Remove(trackId);
                        }
                        
                        failedCount++;
                        envStreak = environmental ? envStreak + 1 : 0;
                        
                        if (envStreak >= 3)
                        {
                            Log.Warning("[DownloadService] Stopping playlist after {N} consecutive {Kind} failures", envStreak, kind);
                            ToastService.Instance.Show(
                                $"Playlist paused: {kind}. Fix this and re-import. Remaining tracks were not touched.",
                                ToastType.Warning, scope: "download");
                            break;
                        }
                    }
                    else if (!string.IsNullOrEmpty(finalFilePath))
                    {
                        envStreak = 0;
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

    private string? FindMostRecentUnlinkedDownload(string? expectedTitle = null, string? videoId = null)
    {
        var dir = new DirectoryInfo(_downloadDir);
        if (!dir.Exists) return null;

        var linkedPaths = _libraryService.GetAll()
            .Where(t => !string.IsNullOrEmpty(t.FilePath))
            .Select(t => Path.GetFullPath(t.FilePath!))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // FIX: Try to find by YouTube video ID first (ASCII, bypasses encoding issues)
        if (!string.IsNullOrEmpty(videoId))
        {
            var tag = $"[{videoId}]";
            var byId = dir.GetFiles()
                .Where(f => AudioExts.Contains(f.Extension.ToLowerInvariant())
                            && f.Name.Contains(tag, StringComparison.Ordinal)
                            && !linkedPaths.Contains(f.FullName))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();
            if (byId != null)
            {
                Log.Information("[DownloadService] Recovered output by video id: {Path}", byId.FullName);
                return byId.FullName;
            }
        }

        FileInfo? newest = null;
        foreach (var file in dir.GetFiles())
        {
            if (!AudioExts.Contains(file.Extension.ToLowerInvariant())) continue;
            if (linkedPaths.Contains(file.FullName)) continue;
            if (newest == null || file.LastWriteTimeUtc > newest.LastWriteTimeUtc) newest = file;
        }

        if (newest == null) return null;

        if (!string.IsNullOrWhiteSpace(expectedTitle))
        {
            static string Norm(string s) => new string(s.Where(char.IsLetterOrDigit).ToArray());
            var want = Norm(expectedTitle);
            var match = dir.GetFiles()
                .Where(f => !linkedPaths.Contains(f.FullName) && AudioExts.Contains(f.Extension.ToLowerInvariant()))
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
    internal int AvailablePermits => _gate.CurrentCount;
}