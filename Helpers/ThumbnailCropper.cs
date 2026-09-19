using System;
using System.IO;
using Serilog;
using SkiaSharp;

namespace NullWave.Helpers;

/// <summary>
/// Normalizes downloaded thumbnails (YouTube 4:3 files with baked-in letterbox bars,
/// or plain 16:9) into clean center-cropped 1:1 squares so square UI tiles never
/// show dark bands. Idempotent: already-square files are left untouched.
/// </summary>
public static class ThumbnailCropper
{
    private const byte NearBlack = 16;          // per-channel "near black" cutoff
    private const double BlackRatio = 0.985;    // fraction of sampled pixels that must be near-black
    private const int SampleStep = 4;           // scan every Nth pixel for speed

    /// <summary>Crops the image at <paramref name="path"/> to a square, in place. Returns true if rewritten.</summary>
    public static bool CropFileToSquare(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;

            using var src = SKBitmap.Decode(path);
            if (src == null || src.Width < 8 || src.Height < 8) return false;

            // 1) Trim baked-in letterbox (top/bottom) and pillarbox (left/right) bars
            var content = TrimLetterbox(src);

            // 2) Already square (within 2%)? Nothing to do - never re-encode needlessly.
            var tolerance = Math.Max(2, (int)(src.Height * 0.02));
            if (Math.Abs(content.Width - content.Height) <= tolerance) return false;

            // 3) Center-crop to a square
            int side = Math.Min(content.Width, content.Height);
            int cx = content.Left + (content.Width - side) / 2;
            int cy = content.Top + (content.Height - side) / 2;
            var srcRect = new SKRect(cx, cy, cx + side, cy + side);

            using var square = new SKBitmap(side, side);
            using (var canvas = new SKCanvas(square))
            {
                canvas.DrawBitmap(src, srcRect, new SKRect(0, 0, side, side));
            }

            // 4) Re-encode to a temp file, then swap atomically
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

        // Safety: genuinely dark artwork (album art that is mostly black) must not be
        // mistaken for letterbox. If we "trimmed" more than 35% of either axis, bail
        // and use the full frame.
        if (h < bmp.Height * 0.65 || w < bmp.Width * 0.65)
            return new SKRectI(0, 0, bmp.Width, bmp.Height);

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