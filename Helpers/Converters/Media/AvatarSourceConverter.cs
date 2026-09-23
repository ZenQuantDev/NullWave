using System;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace NullWave.Helpers.Converters;

/// Accepts a file path (string) OR an existing IImage and returns a renderable IImage.
public class AvatarSourceConverter : IValueConverter
{
    public static readonly AvatarSourceConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        IImage img => img,
        string path when !string.IsNullOrWhiteSpace(path) && File.Exists(path) => SafeBitmap(path),
        _ => null
    };

    private static Bitmap? SafeBitmap(string path)
    {
        try { return new Bitmap(path); } catch { return null; }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}