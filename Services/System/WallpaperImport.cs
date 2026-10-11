using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace NullWave.Services;

public enum WallpaperImageFormat { Unknown, Png, Jpeg, Webp }

public enum WallpaperImportStatus { Ok, Empty, UnsupportedFormat, TooLarge, DecodeFailed, IoError }

public sealed record WallpaperImportResult(WallpaperImportStatus Status, string? Path)
{
    public bool Ok => Status == WallpaperImportStatus.Ok;
}

public static class WallpaperImport
{
    public const long MaxBytes = 25L * 1024 * 1024;
    private const int HeaderLength = 12;

    public static WallpaperImageFormat Sniff(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 8 && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47
            && header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
            return WallpaperImageFormat.Png;

        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
            return WallpaperImageFormat.Jpeg;

        if (header.Length >= HeaderLength && header[0] == (byte)'R' && header[1] == (byte)'I' && header[2] == (byte)'F' && header[3] == (byte)'F'
            && header[8] == (byte)'W' && header[9] == (byte)'E' && header[10] == (byte)'B' && header[11] == (byte)'P')
            return WallpaperImageFormat.Webp;

        return WallpaperImageFormat.Unknown;
    }

    public static string ExtensionFor(WallpaperImageFormat format) => format switch
    {
        WallpaperImageFormat.Png => ".png",
        WallpaperImageFormat.Jpeg => ".jpg",
        WallpaperImageFormat.Webp => ".webp",
        _ => string.Empty
    };

    public static async Task<WallpaperImportResult> ImportAsync(
        Stream source,
        Func<string, string> reserveDestination,
        Func<Stream, bool> canDecode,
        long maxBytes = MaxBytes,
        CancellationToken ct = default)
    {
        string? destination = null;
        try
        {
            var header = new byte[HeaderLength];
            var headerRead = 0;
            while (headerRead < HeaderLength)
            {
                var read = await source.ReadAsync(header.AsMemory(headerRead, HeaderLength - headerRead), ct);
                if (read == 0) break;
                headerRead += read;
            }

            if (headerRead == 0) return new WallpaperImportResult(WallpaperImportStatus.Empty, null);
            var format = Sniff(header.AsSpan(0, headerRead));
            if (format == WallpaperImageFormat.Unknown)
                return new WallpaperImportResult(WallpaperImportStatus.UnsupportedFormat, null);

            if (source.CanSeek && source.Length > maxBytes)
                return new WallpaperImportResult(WallpaperImportStatus.TooLarge, null);

            destination = reserveDestination(ExtensionFor(format));
            long total = headerRead;
            await using (var output = File.Create(destination))
            {
                await output.WriteAsync(header.AsMemory(0, headerRead), ct);
                var buffer = new byte[81920];
                int read;
                while ((read = await source.ReadAsync(buffer.AsMemory(), ct)) > 0)
                {
                    total += read;
                    if (total > maxBytes) break;
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                }
            }

            if (total > maxBytes)
            {
                TryDelete(destination);
                return new WallpaperImportResult(WallpaperImportStatus.TooLarge, null);
            }

            var path = destination;
            var decodable = await Task.Run(() =>
            {
                try
                {
                    using var stream = File.OpenRead(path);
                    return canDecode(stream);
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "[WallpaperImport] Decode probe failed for {Path}", path);
                    return false;
                }
            }, ct);

            if (!decodable)
            {
                TryDelete(destination);
                return new WallpaperImportResult(WallpaperImportStatus.DecodeFailed, null);
            }

            return new WallpaperImportResult(WallpaperImportStatus.Ok, destination);
        }
        catch (OperationCanceledException)
        {
            TryDelete(destination);
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Log.Warning(ex, "[WallpaperImport] Import failed");
            TryDelete(destination);
            return new WallpaperImportResult(WallpaperImportStatus.IoError, null);
        }
    }

    private static void TryDelete(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try { File.Delete(path); }
        catch (Exception ex) { Log.Debug(ex, "[WallpaperImport] Could not delete {Path}", path); }
    }
}

public static class WallpaperStatus
{
    public static (string FormatKey, string? Arg) Describe(string style, string? sceneName, string? builtInName)
        => style switch
        {
            "None" => ("Settings_Appearance_Wallpaper_Off", null),
            "Scene" => ("Settings_Appearance_Wallpaper_ActiveFmt",
                string.IsNullOrWhiteSpace(sceneName) ? style : sceneName),
            "BuiltIn" => ("Settings_Appearance_Wallpaper_ActiveFmt",
                string.IsNullOrWhiteSpace(builtInName) ? style : builtInName),
            _ => ("Settings_Appearance_Wallpaper_ActiveFmt", style)
        };
}