using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace NullWave.Helpers.Converters;

public class BoolToColorConverter : IValueConverter
{
    public static readonly BoolToColorConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not bool b) return GetBrush("BrushTextSecondary");
        
        if (parameter is string param)
        {
            return param switch
            {
                "Accent,Secondary" => b ? GetBrush("BrushAccent") : GetBrush("BrushTextSecondary"),
                _ => b ? GetBrush("BrushAccent") : GetBrush("BrushTextSecondary")
            };
        }
        
        return b ? GetBrush("BrushAccent") : GetBrush("BrushTextSecondary");
    }

    private static IBrush GetBrush(string key)
    {
        if (Application.Current?.Resources.TryGetResource(key, null, out var brush) == true && brush is IBrush b)
            return b;
        return Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}