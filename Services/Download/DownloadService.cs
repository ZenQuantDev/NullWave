using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NullWave.Helpers;
using NullWave.Models;
using Serilog;

namespace NullWave.Services;

public class DownloadService
{
    private readonly string _downloadDir;
    private readonly LibraryService _libraryService;

    public event Action<string, float>? ProgressChanged;
    public event Action<string, string, bool>? DownloadCompleted;
    public event Action<string, string, bool>? DownloadFailed;

    private static readonly Regex ProgressRegex = new(
        @"\[download\]\s+([\d.]+)%", RegexOptions.Compiled);

    private readonly HashSet<string> _activeDownloads = new();
    private CancellationTokenSource? _currentDownloadCts;

    private SemaphoreSlim _semaphore = new(2, 5);
    private int _currentLimit = 2;

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

    public DownloadService(LibraryService libraryService)
    {
        _libraryService = libraryService;
        _downloadDir = NullWavePaths.DownloadsDir;
        Directory.CreateDirectory(_downloadDir);
    }

    public void CancelCurrentDownload()
    {
        _currentDownloadCts?.Cancel();
        Log.Debug("[DownloadService] Current download cancelled by caller");
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

        Log.Information("Starting download: {Url} (format={Format}, quality={Quality})",
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
                FileName               = "yt-dlp",
                Arguments              = string.Join(" ", args),
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true
            };

            using var process = new Process { StartInfo = psi };
            string? outputFilePath = null;

            process.OutputDataReceived += (_, e) =>
            {
                if (string.IsNullOrEmpty(e.Data)) return;

                if (e.Data.StartsWith("/") || e.Data.StartsWith("~"))
                {
                    outputFilePath = e.Data.Trim();
                    return;
                }

                var match = ProgressRegex.Match(e.Data);
                if (match.Success &&
                    float.TryParse(match.Groups[1].Value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var pct))
                {
                    ProgressChanged?.Invoke(trackId, pct / 100f);
                }

                Log.Debug("yt-dlp: {Line}", e.Data);
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    Log.Warning("yt-dlp stderr: {Line}", e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(ct);

            if (process.ExitCode == 0 && outputFilePath != null && File.Exists(outputFilePath))
            {
                Log.Information("Download complete: {Path}", outputFilePath);
                DownloadCompleted?.Invoke(trackId, outputFilePath, isInteractive);
            }
            else if (process.ExitCode == 0)
            {
                var recent = FindMostRecentDownload();
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
                FileName               = "yt-dlp",
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
            var lines = metadataOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;

                    var videoId = root.TryGetProperty("id", out var idProp)
                        ? idProp.GetString() ?? "" : "";

                    var rawTitle = root.TryGetProperty("title", out var titleProp)
                        ? titleProp.GetString() ?? "Unknown Track" : "Unknown Track";

                    // ── Try every field yt-dlp may provide for artist name ──────────
                    // Priority order: "artist" (best) → "creator" → "uploader"
                    // → "channel" (worst - often "Lady Gaga - Topic")
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

                    // Strip " - Topic" suffix that YouTube appends to official artist channels
                    artist = Regex.Replace(artist, @"\s*-\s*Topic\s*$", "",
                        RegexOptions.IgnoreCase).Trim();

                    // If we still have no useful artist, try splitting the title
                    // e.g. "Lady Gaga - Just Dance" → artist="Lady Gaga", title="Just Dance"
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
                    }
                    else if (!string.IsNullOrEmpty(finalFilePath))
                    {
                        var dbTrack = _libraryService.GetAll().FirstOrDefault(t => t.Id == trackId);
                        if (dbTrack != null)
                        {
                            dbTrack.FilePath = finalFilePath;

                            // ── Clean up YouTube channel artist names before enrichment ─────────
                            if (!string.IsNullOrEmpty(dbTrack.Artist))
                            {
                                var cleanArtist = Regex.Replace(
                                    dbTrack.Artist,
                                    @"\s*-\s*Topic\s*$",
                                    string.Empty,
                                    RegexOptions.IgnoreCase).Trim();

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

                            // Also strip clutter from the title itself
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

                            _libraryService.Update(dbTrack);

                            Log.Debug("[DownloadService] Track ready: '{Title}' by '{Artist}' → {Path}",
                                dbTrack.Title, dbTrack.Artist, finalFilePath);

                            onTrackReady?.Invoke(dbTrack);
                        }
                    }

                    if (i < tracks.Count - 1) 
                    {
                        var delayMs = Random.Shared.Next(3000, 8000);
                        Log.Information("Throttling download to avoid rate limits... sleeping for {Delay}ms", delayMs);
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
                    Log.Warning("Skipping unavailable track {Url}: {Msg}", cleanUrl, ex.Message);
                    _libraryService.Remove(trackId);
                    DownloadCompleted -= OnCompleted;
                    DownloadFailed    -= OnFailed;
                    continue;
                }
            }

            Log.Information("Playlist download complete");
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

    private string? FindMostRecentDownload()
    {
        var dir = new DirectoryInfo(_downloadDir);
        if (!dir.Exists) return null;
        FileInfo? newest = null;
        foreach (var file in dir.GetFiles("*.mp3"))
            if (newest == null || file.LastWriteTime > newest.LastWriteTime)
                newest = file;
        return newest?.FullName;
    }

    public string DownloadDirectory => _downloadDir;
}