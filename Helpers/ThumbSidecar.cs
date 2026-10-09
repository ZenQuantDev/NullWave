using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using SkiaSharp;

namespace NullWave.Helpers;

/// <summary>
/// Disk-level pre-scale cache. The first time a full-size artwork is decoded we
/// re-encode it once via SkiaSharp into a small JPEG sidecar ("{path}.nws.jpg",
/// 256px wide). Every later cold decode (startup warm, cold scroll, next session)
/// reads the sidecar instead of the multi-megapixel original: ~10x less disk I/O
/// and a sampled decode that finishes in ~1ms. Sidecars are pure cache: safe to
/// delete anytime; InvalidatePath removes them automatically.
/// </summary>
public static class ThumbSidecar
{
    public const int SidecarWidth = 256;
    private const int JpegQuality = 88;
    private static readonly SemaphoreSlim BuildGate = new(2, 2);
    private static readonly ConcurrentDictionary<string, byte> ScheduledBuilds = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    public static string SidecarPath(string path) => path + ".nws.jpg";

    /// <summary>Returns the sidecar when it can satisfy <paramref name="width"/>, else null.</summary>
    public static string? UsableSidecar(string path, int width)
    {
        if (width <= 0 || width > SidecarWidth) return null;
        var sc = SidecarPath(path);
        return File.Exists(sc) ? sc : null;
    }

    public static void DeleteFor(string path)
    {
        try { File.Delete(SidecarPath(path)); } catch { /* cache only */ }
    }

    /// <summary>Fire-and-forget sidecar build; gated so idle builds never saturate cores.</summary>
    public static void ScheduleBuild(string path)
    {
        if (UsableSidecar(path, 1) != null || !ScheduledBuilds.TryAdd(path, 0)) return;
        _ = Task.Run(() =>
        {
            try { Build(path); }
            finally { ScheduledBuilds.TryRemove(path, out _); }
        });
    }

    private static bool Build(string path)
    {
        if (!File.Exists(path)) return false;
        BuildGate.Wait();
        try
        {
            if (UsableSidecar(path, 1) != null) return false; // lost a race: already built

            using var src = SKBitmap.Decode(path);
            if (src == null || src.Width <= SidecarWidth) return false;
            if (src.Info.AlphaType != SKAlphaType.Opaque) return false; // JPEG sidecar would bake transparency: skip

            var h = (int)Math.Round(src.Height * (SidecarWidth / (double)src.Width));
            var info = new SKImageInfo(SidecarWidth, h, SKColorType.Rgba8888, SKAlphaType.Premul);
            // SkiaSharp 2.88 fallback: src.Resize(info, SKFilterQuality.Medium)
            using var scaled = src.Resize(info, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            if (scaled == null) return false;

            var tmp = SidecarPath(path) + ".tmp";
            using (var data = scaled.Encode(SKEncodedImageFormat.Jpeg, JpegQuality))
            using (var fs = File.Create(tmp))
                data.SaveTo(fs);
            File.Move(tmp, SidecarPath(path), overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[ThumbSidecar] build failed for {Path}", path);
            try { File.Delete(SidecarPath(path) + ".tmp"); } catch { }
            return false;
        }
        finally { BuildGate.Release(); }
    }
}