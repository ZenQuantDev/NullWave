using System;
using System.Globalization;
using System.IO;
using Avalonia;
using Avalonia.Data.Converters;

namespace NullWave.Helpers.Converters;

public class FilePathToBitmapConverter : IValueConverter
{
    // Display-size decode for single-instance views (detail art renders <= ~320px wide).
    private const int DecodeWidth = 256;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrEmpty(path) || !File.Exists(path))
            return null;
        return BitmapCacheService.DecodeSync(path, DecodeWidth);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => AvaloniaProperty.UnsetValue;
}