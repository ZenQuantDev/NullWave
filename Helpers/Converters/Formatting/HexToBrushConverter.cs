using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace NullWave.Helpers.Converters;

public class HexToBrushConverter : IValueConverter
{
    public static readonly HexToBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string s && s.StartsWith("#"))
        {
            try { return new SolidColorBrush(Color.Parse(s)); }
            catch { return null; }
        }
        return null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}