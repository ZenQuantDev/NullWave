using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace NullWave.Helpers.Converters;

public class FilePathToBitmapConverter : IValueConverter
{
    private const int MaxCacheSize = 500;
    private static readonly object CacheLock = new();
    private static readonly Dictionary<string, Bitmap> Cache = new();
    private static readonly LinkedList<string> Lru = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrEmpty(path) || !File.Exists(path))
            return null;

        lock (CacheLock)
        {
            if (Cache.TryGetValue(path, out var cached))
            {
                Lru.Remove(path);
                Lru.AddFirst(path);
                return cached;
            }
        }

        try
        {
            var bitmap = new Bitmap(path);
            lock (CacheLock)
            {
                if (Cache.TryGetValue(path, out var existing))
                {
                    bitmap.Dispose();
                    Lru.Remove(path);
                    Lru.AddFirst(path);
                    return existing;
                }

                if (Cache.Count >= MaxCacheSize && Lru.Last != null)
                {
                    var oldest = Lru.Last.Value;
                    Lru.RemoveLast();
                    if (Cache.Remove(oldest, out var evicted))
                        evicted.Dispose();
                }

                Cache[path] = bitmap;
                Lru.AddFirst(path);
                return bitmap;
            }
        }
        catch
        {
            return null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return AvaloniaProperty.UnsetValue;
    }
}