using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace NullWave.Helpers.Converters;

public class FrameBorderBrushConverter : IValueConverter
{
    public static readonly FrameBorderBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var style = value as string;
        if (style == "None" || string.IsNullOrEmpty(style)) 
            return Brushes.Transparent;
        
        // Duo-tone gradient using the active accent colors
        var res = Application.Current?.Resources;
        var c1 = res?["ColorAccent"] is Color col1 ? col1 : Color.Parse("#4E9BFF");
        var c2 = res?["ColorAccent2"] is Color col2 ? col2 : Color.Parse("#B08CFF");
        
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = new GradientStops { new GradientStop(c1, 0), new GradientStop(c2, 1) }
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}