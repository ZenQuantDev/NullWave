using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace NullWave.Helpers.Converters;

/// <summary>
/// Converts a bool to a GridLength for collapsible Pixel columns. Parameter format:
/// "collapsedPixels,expandedPixels" (e.g. "0,220"). Used for sidebar collapse — NOT
/// a general-purpose GridLength converter, since it only handles the two-state case.
/// </summary>
public class BoolToGridLengthConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not bool isCollapsed) return GridLength.Auto;
        var parts = (parameter as string)?.Split(',') ?? new[] { "0", "220" };
        var collapsedWidth = double.Parse(parts[0], CultureInfo.InvariantCulture);
        var expandedWidth = double.Parse(parts[1], CultureInfo.InvariantCulture);
        return new GridLength(isCollapsed ? collapsedWidth : expandedWidth);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}