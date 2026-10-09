using System;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Serilog;

namespace NullWave.Helpers.Attached;

/// <summary>
/// Async image source for virtualized lists (v4).
/// v2: skip redundant Source writes on recycle. 
/// v3: bounded hit/miss instrumentation.
/// v4: default decode width matches the 96px row slot exactly (no draw-time
/// scaling), and cold-miss completions apply Source on the UI thread.
/// </summary>
public class AsyncImage
{
    private AsyncImage() { }

    public static readonly AttachedProperty<string?> SourcePathProperty =
        AvaloniaProperty.RegisterAttached<AsyncImage, Image, string?>("SourcePath");

    public static readonly AttachedProperty<int> DecodeWidthProperty =
        AvaloniaProperty.RegisterAttached<AsyncImage, Image, int>("DecodeWidth", 96);

    private static int _hits;
    private static int _misses;

    static AsyncImage()
    {
        SourcePathProperty.Changed.AddClassHandler<Image>(OnSourcePathChanged);
    }

    public static void SetSourcePath(Image image, string? value) => image.SetValue(SourcePathProperty, value);
    public static string? GetSourcePath(Image image) => image.GetValue(SourcePathProperty);
    public static void SetDecodeWidth(Image image, int value) => image.SetValue(DecodeWidthProperty, value);
    public static int GetDecodeWidth(Image image) => image.GetValue(DecodeWidthProperty);

    private static async void OnSourcePathChanged(Image image, AvaloniaPropertyChangedEventArgs e)
    {
        var path = e.NewValue as string;
        if (string.IsNullOrEmpty(path)) { image.Source = null; return; }

        var width = EffectiveWidth(image);

        var cached = BitmapCacheService.TryGet(path, width);
        if (cached != null)
        {
            var hits = Interlocked.Increment(ref _hits);
            if (hits % 250 == 0)
                Log.Debug("[AsyncImage] cache warm: hits={Hits} misses={Misses}", hits, Volatile.Read(ref _misses));
            if (!ReferenceEquals(image.Source, cached)) image.Source = cached;
            return;
        }

        var misses = Interlocked.Increment(ref _misses);
        if (misses <= 5 || misses % 25 == 0)
            Log.Debug("[AsyncImage] CACHE MISS #{Misses}: {Path} @{Width}px", misses, path, width);

        image.Source = null; // placeholder only on cold-cache miss
        var bmp = await BitmapCacheService.GetOrDecodeAsync(path, width);
        if (bmp == null) return;

        if (Dispatcher.UIThread.CheckAccess())
            Apply(image, path, bmp);
        else
            Dispatcher.UIThread.Post(() => Apply(image, path, bmp), DispatcherPriority.Background);
    }

    private static int EffectiveWidth(Image image)
    {
        var width = GetDecodeWidth(image);
        var scale = TopLevel.GetTopLevel(image)?.RenderScaling ?? 1.0;
        return scale <= 1.0 ? width : (int)Math.Ceiling(width * scale);
    }

    private static void Apply(Image image, string path, IImage bmp)
    {
        if (GetSourcePath(image) == path && !ReferenceEquals(image.Source, bmp))
            image.Source = bmp;
    }
}