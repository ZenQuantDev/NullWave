using System;
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
            return artPath;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Thumbnail download failed for {Url}", url);
            return null;
        }
    }
}