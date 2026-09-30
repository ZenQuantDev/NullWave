using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using NullWave.Helpers;
using Serilog;

namespace NullWave.Services.Metadata;

public static class ThumbnailDownloader
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(8)
    };
    
    // Remembers URLs that returned 404 so we don't probe them again.
    private static readonly ConcurrentDictionary<string, bool> NegativeCache = new();

    // Disk sidecar so restarts after a purge don't re-walk the 4-URL ladder
    // for URLs already known to be dead.
    private static readonly string NegCachePath = Path.Combine(NullWavePaths.DataDir, "thumb-404-cache.json");
    private static DateTime _lastNegPersist = DateTime.MinValue;
    private const int NegCacheMaxEntries = 5000;
    private const int NegCachePersistIntervalSeconds = 30;

    static ThumbnailDownloader()
    {
        try
        {
            if (File.Exists(NegCachePath))
            {
                foreach (var line in File.ReadAllLines(NegCachePath))
                {
                    if (!string.IsNullOrWhiteSpace(line))
                        NegativeCache[line.Trim()] = true;
                }
                Log.Debug("[ThumbnailDownloader] Loaded {Count} negative-cache entries from disk.", NegativeCache.Count);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[ThumbnailDownloader] Failed to load negative cache from disk; starting empty.");
        }
    }

    private static void PersistNegativeCache()
    {
        if ((DateTime.UtcNow - _lastNegPersist).TotalSeconds < NegCachePersistIntervalSeconds) return;
        _lastNegPersist = DateTime.UtcNow;
        try
        {
            // Bounded write: keep only the most-recently-added entries up to the cap.
            var snapshot = NegativeCache.Keys.Take(NegCacheMaxEntries).ToArray();
            File.WriteAllLines(NegCachePath, snapshot);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[ThumbnailDownloader] Failed to persist negative cache; will retry next interval.");
        }
    }

    public static async Task<string?> FetchAsync(string url, string cacheKey)
    {
        if (NegativeCache.ContainsKey(url))
        {
            Log.Debug("Skipping known 404 URL in FetchAsync: {Url}", url);
            return null;
        }

        try
        {
            var ext = Path.GetExtension(url.Split('?')[0]);
            if (string.IsNullOrEmpty(ext)) ext = ".jpg";

            var artPath = Path.Combine(NullWavePaths.ArtCacheDir, $"{cacheKey}{ext}");
            if (File.Exists(artPath)) return artPath;

            var bytes = await Http.GetByteArrayAsync(url);

            if (bytes.Length < 2048)
            {
                Log.Debug("Thumbnail too small (placeholder?), skipping: {Url}", url);
                return null;
            }

            await File.WriteAllBytesAsync(artPath, bytes);
            Log.Information("Thumbnail saved: {Path}", artPath);

            ThumbnailCropper.TrimLetterboxInPlace(artPath);

            return artPath;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            NegativeCache.TryAdd(url, true);
            PersistNegativeCache();
            Log.Debug("Thumbnail not found at {Url} (cached as 404)", url);
            return null;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Thumbnail download failed for {Url}", url);
            return null;
        }
    }

    public static async Task<string?> FetchWithFallbackAsync(IEnumerable<string> urls, string cacheKey)
    {
        var artPath = Path.Combine(NullWavePaths.ArtCacheDir, $"{cacheKey}.jpg");

        if (File.Exists(artPath)) return artPath;

        foreach (var url in urls)
        {
            if (NegativeCache.ContainsKey(url)) 
            {
                Log.Debug("Skipping known 404 URL: {Url}", url);
                continue; 
            }

            try
            {
                var bytes = await Http.GetByteArrayAsync(url);

                if (bytes.Length < 2048)
                {
                    Log.Debug("Thumbnail too small (placeholder?), skipping: {Url}", url);
                    continue;
                }

                await File.WriteAllBytesAsync(artPath, bytes);
                Log.Information("High-res thumbnail saved: {Path}", artPath);

                ThumbnailCropper.TrimLetterboxInPlace(artPath);

                return artPath;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                NegativeCache.TryAdd(url, true);
                PersistNegativeCache();
                Log.Debug("Thumbnail not found at {Url}, trying fallback...", url);
                continue;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Thumbnail download failed for {Url}", url);
                continue;
            }
        }

        return null;
    }
}