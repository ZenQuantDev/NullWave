using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using NullWave.Helpers;
using Serilog;

namespace NullWave.Services.Metadata;

public static class ThumbnailDownloader
{
    private static readonly HttpClient Http = new();

    /// <summary>
    /// Downloads a thumbnail from a URL and caches it to the art directory.
    /// Returns the local file path, or null if download fails.
    /// </summary>
    public static async Task<string?> FetchAsync(string url, string cacheKey)
    {
        try
        {
            var ext      = Path.GetExtension(url.Split('?')[0]);
            if (string.IsNullOrEmpty(ext)) ext = ".jpg";
            var artPath  = Path.Combine(NullWavePaths.ArtCacheDir, $"{cacheKey}{ext}");

            if (File.Exists(artPath)) return artPath;

            var bytes = await Http.GetByteArrayAsync(url);

            // Skip placeholder images smaller than 2KB
            if (bytes.Length < 2048)
            {
                Log.Debug("Thumbnail too small (placeholder?), skipping: {Url}", url);
                return null;
            }

            await File.WriteAllBytesAsync(artPath, bytes);
            Log.Information("Thumbnail saved: {Path}", artPath);

            // Square-crop the freshly downloaded thumbnail to eliminate YouTube's
            // baked-in 4:3 letterbox bars. Idempotent: already-square files are left
            // untouched by ThumbnailCropper.
            ThumbnailCropper.TrimLetterboxInPlace(artPath);

            return artPath;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Thumbnail download failed for {Url}", url);
            return null;
        }
    }
    
    /// <summary>
    /// Tries a sequence of URLs in order, returning the first successful high-res download.
    /// Handles 404s gracefully for YouTube's maxresdefault endpoints.
    /// </summary>
    public static async Task<string?> FetchWithFallbackAsync(IEnumerable<string> urls, string cacheKey)
    {
        // Always cache as .jpg since ThumbnailCropper normalizes the output to JPEG
        var artPath = Path.Combine(NullWavePaths.ArtCacheDir, $"{cacheKey}.jpg");
        
        // If we already have a cached version, skip the network entirely
        if (File.Exists(artPath)) return artPath;

        foreach (var url in urls)
        {
            try
            {
                var bytes = await Http.GetByteArrayAsync(url);
                
                // Skip placeholder images smaller than 2KB
                if (bytes.Length < 2048)
                {
                    Log.Debug("Thumbnail too small (placeholder?), skipping: {Url}", url);
                    continue;
                }

                await File.WriteAllBytesAsync(artPath, bytes);
                Log.Information("High-res thumbnail saved: {Path}", artPath);

                // Square-crop and re-encode as high-quality JPEG
                ThumbnailCropper.TrimLetterboxInPlace(artPath);
                return artPath;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // YouTube returns 404 for maxresdefault if the video doesn't have one. Try the next fallback.
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