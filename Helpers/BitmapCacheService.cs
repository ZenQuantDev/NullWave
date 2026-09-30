using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Serilog;

namespace NullWave.Helpers;

public static class BitmapCacheService
{
    private static readonly object Lock = new();

    // Wrapper to hold the LinkedListNode for O(1) LRU updates
    private class CacheEntry
    {
        public SharedImage Image { get; }
        public LinkedListNode<string> Node { get; }
        public CacheEntry(SharedImage image, LinkedListNode<string> node)
        {
            Image = image;
            Node = node;
        }
    }

    private static readonly Dictionary<string, CacheEntry> Cache = new();
    private static readonly LinkedList<string> Lru = new();
    private const int MaxEntries = 800;

    private static string Key(string path, int width) => width <= 0 ? path : $"{path}@{width}";
    private static readonly System.Threading.SemaphoreSlim DecodeGate = new(4, 4);

    /// <summary>Drops every decoded bitmap (after purge/re-crop). Never Disposes:
    /// live Image controls may still hold wrappers as Source.</summary>
    public static void Clear()
    {
        lock (Lock)
        {
            Cache.Clear();
            Lru.Clear();
        }
    }

    /// <summary>
    /// Drops every cached decode for a given file path (all widths). Used by the
    /// YouTube-thumb heal: the file is deleted and re-downloaded to the SAME path,
    /// so stale decoded bitmaps must not survive the swap.
    /// </summary>
    public static void InvalidatePath(string path)
    {
        lock (Lock)
        {
            var keys = Cache.Keys
                .Where(k => k == path || k.StartsWith(path + "@", System.StringComparison.Ordinal))
                .ToList();
            foreach (var key in keys)
            {
                Cache.Remove(key);
                var node = Lru.Find(key);
                if (node != null) Lru.Remove(node);
            }
        }
    }

    public static IImage? TryGet(string path, int width)
    {
        lock (Lock)
        {
            if (!Cache.TryGetValue(Key(path, width), out var entry)) return null;

            // O(1) LRU update: move existing node to front
            if (entry.Node != Lru.First)
            {
                Lru.Remove(entry.Node);
                Lru.AddFirst(entry.Node);
            }
            return entry.Image;
        }
    }

    public static async Task<IImage?> GetOrDecodeAsync(string path, int width)
    {
        // 1. Fast path: already in cache
        var cached = TryGet(path, width);
        if (cached != null) return cached;

        // 2. Slow path: decode off-thread, gated to prevent completion bursts
        try
        {
            var bmp = await Task.Run(async () =>
            {
                await DecodeGate.WaitAsync().ConfigureAwait(false);
                try { return Decode(path, width); }
                finally { DecodeGate.Release(); }
            }).ConfigureAwait(false);

            if (bmp == null) return null;

            // Insert creates the wrapper under the lock; on a duplicate-decode race
            // it disposes the NEW bitmap and returns the canonical live wrapper.
            return Insert(path, width, bmp);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Synchronous path for single-instance views (converters, detail panel).</summary>
    public static IImage? DecodeSync(string path, int width)
    {
        var cached = TryGet(path, width);
        if (cached != null) return cached;

        var bmp = Decode(path, width);
        if (bmp == null) return null;

        return Insert(path, width, bmp);
    }

    private static Bitmap? Decode(string path, int width)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        try
        {
            if (width > 0)
            {
                using var fs = File.OpenRead(path);
                return Bitmap.DecodeToWidth(fs, width);
            }
            return new Bitmap(path);
        }
        catch
        {
            try { return new Bitmap(path); } catch { return null; }
        }
    }

    /// <summary>
    /// Registers a decoded bitmap and returns the canonical wrapper. If another thread
    /// won the race for this key, the incoming rawBmp is disposed HERE - before any
    /// wrapper ever references it - and the existing live wrapper is returned instead.
    /// Eviction never disposes: an evicted wrapper may still be an Image.Source.
    /// </summary>
    private static SharedImage Insert(string path, int width, Bitmap rawBmp)
    {
        var key = Key(path, width);
        lock (Lock)
        {
            if (Cache.TryGetValue(key, out var existing))
            {
                rawBmp.Dispose(); // duplicate decode: discard the newcomer, keep canonical
                return existing.Image;
            }

            if (Cache.Count >= MaxEntries && Lru.Last != null)
            {
                var oldKey = Lru.Last.Value;
                Lru.RemoveLast();
                Cache.Remove(oldKey); // no Dispose: may still be on screen
            }

            var wrapper = new SharedImage(rawBmp);
            var node = Lru.AddFirst(key);
            Cache[key] = new CacheEntry(wrapper, node);
            return wrapper;
        }
    }
}