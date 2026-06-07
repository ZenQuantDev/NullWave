using System;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace NullWave.Helpers.Converters;

public class FilePathToBitmapConverter : IValueConverter
{
    public static readonly FilePathToBitmapConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrEmpty(path)) return null;
        if (!File.Exists(path)) return null;
        try { return new Bitmap(path); }
        catch { return null; }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}