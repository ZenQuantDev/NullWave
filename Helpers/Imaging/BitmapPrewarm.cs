using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Serilog;

namespace NullWave.Helpers;

/// <summary>
/// Forces GPU texture realization for cached bitmaps BEFORE the user scrolls.
/// Decoded Bitmaps live CPU-side; Skia uploads them to a GPU texture lazily on first
/// draw (compositor thread) - that per-row upload is the hitch seen during the first
/// scroll after startup/purge. Drawing each image once into a scratch
/// RenderTargetBitmap populates the shared Skia GL texture cache, so the first real
/// scroll is upload-free. Chunked at Background priority; one real frame is allowed
/// through between chunks so prewarm never competes with interaction.
/// </summary>
public static class BitmapPrewarm
{
    public static void PrewarmGpuTextures(IReadOnlyList<IImage> images, int batchSize = 24, int textureSize = 48)
    {
        if (images.Count == 0) return;

        _ = Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var sw = Stopwatch.StartNew();
            using var rtb = new RenderTargetBitmap(new PixelSize(textureSize, textureSize));
            int done = 0;

            for (int i = 0; i < images.Count; i += batchSize)
            {
                var end = Math.Min(i + batchSize, images.Count);
                using (var ctx = rtb.CreateDrawingContext())
                {
                    for (int j = i; j < end; j++)
                    {
                        try
                        {
                            var img = images[j];
                            img.Draw(ctx, new Rect(img.Size), new Rect(0, 0, textureSize, textureSize));
                        }
                        catch (Exception ex)
                        {
                            Log.Debug(ex, "[BitmapPrewarm] skipped one image");
                        }
                    }
                }
                done = end;
                // Yield one real render frame between chunks.
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
            }

            Log.Information("[BitmapPrewarm] GPU textures prewarmed: {Count} images in {Ms}ms",
                done, sw.ElapsedMilliseconds);
        }, DispatcherPriority.Background);
    }
}