using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace NullWave.Helpers.Converters;

/// <summary>True → dimmed (0.45), False → full (1.0). Greys out controls overridden by Compact mode.</summary>
public class BoolToDimConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? 0.45 : 1.0;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}