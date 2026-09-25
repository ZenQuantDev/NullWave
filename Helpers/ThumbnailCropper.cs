using System;
using System.IO;
using Serilog;
using SkiaSharp;

namespace NullWave.Helpers;

/// <summary>
/// Normalizes downloaded thumbnails. Trims baked-in letterbox/pillarbox bars but
/// PRESERVES the source aspect ratio: UI controls crop visually via UniformToFill,
/// so forcing squares here destroyed composition and threw away resolution.
/// </summary>
public static class ThumbnailCropper
{
    private const byte NearBlack = 16;          // per-channel "near black" cutoff
    private const double BlackRatio = 0.985;    // fraction of sampled pixels that must be near-black
    private const int SampleStep = 4;           // scan every Nth pixel for speed

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

    /// <summary>Legacy square center-crop. Kept only for existing callers; do not use for new thumbnail paths.</summary>
    public static bool CropFileToSquare(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;

            using var src = SKBitmap.Decode(path);
            if (src == null || src.Width < 8 || src.Height < 8) return false;

            var content = TrimLetterbox(src);
            var tolerance = Math.Max(2, (int)(src.Height * 0.02));
            if (Math.Abs(content.Width - content.Height) <= tolerance) return false;

            int side = Math.Min(content.Width, content.Height);
            int cx = content.Left + (content.Width - side) / 2;
            int cy = content.Top + (content.Height - side) / 2;
            var srcRect = new SKRect(cx, cy, cx + side, cy + side);

            using var square = new SKBitmap(side, side);
            using (var canvas = new SKCanvas(square))
            {
                canvas.DrawBitmap(src, srcRect, new SKRect(0, 0, side, side));
            }

            var tmp = path + ".crop.tmp";
            using (var data = square.Encode(SKEncodedImageFormat.Jpeg, 90))
            using (var fs = File.Create(tmp))
            {
                data.SaveTo(fs);
            }
            File.Move(tmp, path, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[ThumbnailCropper] Failed to crop {Path}", path);
            try { if (File.Exists(path + ".crop.tmp")) File.Delete(path + ".crop.tmp"); } catch { }
            return false;
        }
    }

    private static SKRectI TrimLetterbox(SKBitmap bmp)
    {
        int top = 0, bottom = bmp.Height - 1;
        while (top < bottom && IsBlackRow(bmp, top)) top++;
        while (bottom > top && IsBlackRow(bmp, bottom)) bottom--;

        int left = 0, right = bmp.Width - 1;
        while (left < right && IsBlackColumn(bmp, left, top, bottom)) left++;
        while (right > left && IsBlackColumn(bmp, right, top, bottom)) right--;

        int h = bottom - top + 1;
        int w = right - left + 1;

        // Per-axis safety: genuinely dark artwork must not be mistaken for bars.
        // Accept a trim only if at least half of that axis remains; otherwise keep the full axis.
        // This correctly handles the classic YouTube case (square art in 16:9 frame ~56% width retained)
        // while still protecting album art that is >50% black on either axis.
        if (h < bmp.Height * 0.50) { top = 0; bottom = bmp.Height - 1; }
        if (w < bmp.Width  * 0.50) { left = 0; right = bmp.Width - 1; }

        return new SKRectI(left, top, right + 1, bottom + 1);
    }

    private static bool IsBlackRow(SKBitmap bmp, int y)
    {
        int black = 0, sampled = 0;
        for (int x = 0; x < bmp.Width; x += SampleStep)
        {
            var p = bmp.GetPixel(x, y);
            sampled++;
            if (p.Red < NearBlack && p.Green < NearBlack && p.Blue < NearBlack) black++;
        }
        return sampled > 0 && black / (double)sampled >= BlackRatio;
    }

    private static bool IsBlackColumn(SKBitmap bmp, int x, int top, int bottom)
    {
        int black = 0, sampled = 0;
        for (int y = top; y <= bottom; y += SampleStep)
        {
            var p = bmp.GetPixel(x, y);
            sampled++;
            if (p.Red < NearBlack && p.Green < NearBlack && p.Blue < NearBlack) black++;
        }
        return sampled > 0 && black / (double)sampled >= BlackRatio;
    }
}