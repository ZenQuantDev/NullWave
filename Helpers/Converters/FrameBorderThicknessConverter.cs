using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace NullWave.Helpers.Converters;

public class FrameBorderThicknessConverter : IValueConverter
{
    public static readonly FrameBorderThicknessConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // Added parentheses to fix CS8848 precedence warning
        return (value as string) switch
        {
            "Ring" => new Thickness(3),
            "Glow" => new Thickness(3),
            _ => new Thickness(0)
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}