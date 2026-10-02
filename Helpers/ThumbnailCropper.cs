using System;
using System.IO;
using Serilog;
using SkiaSharp;

namespace NullWave.Helpers;

/// <summary>
/// Normalizes thumbnails. Trims baked-in letterbox/pillarbox bars (black, maroon,
/// dark-grey - any flat dark band) but PRESERVES source aspect ratio: UI controls
/// crop visually via UniformToFill where they want to, and the detail view mats.
/// The legacy square center-crop was REMOVED: it destroyed side pixels on 16:9
/// sources ("zoomed/cut" covers). Aspect-preserving trim + UI-side cropping is
/// the only supported normalization now.
/// </summary>
public static class ThumbnailCropper
{
    private const int SampleStep = 4;        // scan every Nth pixel for speed
    private const int FlatTolerance = 14;    // max per-channel deviation inside a bar band
    private const double FlatRatio = 0.985;  // sampled pixels that must be uniform
    private const int MaxBarLuma = 140;      // bars are dark; protects white/light-bg art

    /// <summary>Trims letterbox/pillarbox bars in place, keeping the original aspect. Returns true if rewritten.</summary>
    public static bool TrimLetterboxInPlace(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;

            using var src = SKBitmap.Decode(path);
            if (src == null || src.Width < 8 || src.Height < 8) return false;

            var content = TrimLetterbox(src);
            if (content.Width >= src.Width && content.Height >= src.Height) return false; // nothing to trim

            using var cropped = new SKBitmap(content.Width, content.Height);
            using (var canvas = new SKCanvas(cropped))
            {
                canvas.DrawBitmap(src,
                    new SKRect(content.Left, content.Top, content.Right, content.Bottom),
                    new SKRect(0, 0, content.Width, content.Height));
            }

            var tmp = path + ".crop.tmp";
            using (var data = cropped.Encode(SKEncodedImageFormat.Jpeg, 90))
            using (var fs = File.Create(tmp))
            {
                data.SaveTo(fs);
            }
            File.Move(tmp, path, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[ThumbnailCropper] Failed to trim {Path}", path);
            try { if (File.Exists(path + ".crop.tmp")) File.Delete(path + ".crop.tmp"); } catch { }
            return false;
        }
    }

    private static SKRectI TrimLetterbox(SKBitmap bmp)
    {
        int top = 0, bottom = bmp.Height - 1;
        while (top < bottom && IsBarRow(bmp, top)) top++;
        while (bottom > top && IsBarRow(bmp, bottom)) bottom--;

        int left = 0, right = bmp.Width - 1;
        while (left < right && IsBarColumn(bmp, left, top, bottom)) left++;
        while (right > left && IsBarColumn(bmp, right, top, bottom)) right--;

        int h = bottom - top + 1;
        int w = right - left + 1;

        // Per-axis safety: genuinely dark artwork must not be mistaken for bars.
        // Accept a trim only if at least half of that axis remains.
        if (h < bmp.Height * 0.50) { top = 0; bottom = bmp.Height - 1; }
        if (w < bmp.Width  * 0.50) { left = 0; right = bmp.Width - 1; }

        return new SKRectI(left, top, right + 1, bottom + 1);
    }

    private static bool IsBarRow(SKBitmap bmp, int y)
    {
        var refPix = bmp.GetPixel(0, y);
        int matched = 0, sampled = 0;
        for (int x = 0; x < bmp.Width; x += SampleStep)
        {
            var p = bmp.GetPixel(x, y);
            if (sampled == 0) refPix = p;
            sampled++;
            if (Math.Abs(p.Red - refPix.Red) <= FlatTolerance &&
                Math.Abs(p.Green - refPix.Green) <= FlatTolerance &&
                Math.Abs(p.Blue - refPix.Blue) <= FlatTolerance) matched++;
        }
        if (sampled == 0 || matched / (double)sampled < FlatRatio) return false;
        return Luma(refPix) <= MaxBarLuma;
    }

    private static bool IsBarColumn(SKBitmap bmp, int x, int top, int bottom)
    {
        var refPix = bmp.GetPixel(x, top);
        int matched = 0, sampled = 0;
        for (int y = top; y <= bottom; y += SampleStep)
        {
            var p = bmp.GetPixel(x, y);
            if (sampled == 0) refPix = p;
            sampled++;
            if (Math.Abs(p.Red - refPix.Red) <= FlatTolerance &&
                Math.Abs(p.Green - refPix.Green) <= FlatTolerance &&
                Math.Abs(p.Blue - refPix.Blue) <= FlatTolerance) matched++;
        }
        if (sampled == 0 || matched / (double)sampled < FlatRatio) return false;
        return Luma(refPix) <= MaxBarLuma;
    }

    private static int Luma(SKColor c) => (c.Red * 299 + c.Green * 587 + c.Blue * 114) / 1000;
}