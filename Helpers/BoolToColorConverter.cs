using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace NullWave.Helpers;

public class BoolToColorConverter : IValueConverter
{
    public static readonly BoolToColorConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true && parameter is Color color)
            return color;
        
        return Colors.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
