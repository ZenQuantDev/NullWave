using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace NullWave.Helpers.Converters;

public class AssetThumbConverter : IValueConverter
{
    public static readonly AssetThumbConverter Instance = new();
    private static readonly Dictionary<string, Bitmap> Cache = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string uri || string.IsNullOrWhiteSpace(uri)) return null;
        if (Cache.TryGetValue(uri, out var cached)) return cached;

        try
        {
            using var stream = AssetLoader.Open(new Uri(uri));
            var thumbnail = Bitmap.DecodeToWidth(stream, 256);
            Cache[uri] = thumbnail;
            return thumbnail;
        }
        catch
        {
            return null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class IsBuiltInLockedConverter : IMultiValueConverter
{
    public static readonly IsBuiltInLockedConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Count >= 2 && values[0] is true && values[1] is false;
}