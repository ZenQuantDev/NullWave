using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Material.Icons;

namespace NullWave.Helpers.Converters;

/// <summary>
/// Converts a boolean value to a MaterialIconKind enum based on a comma-separated parameter.
/// Parameter format: "TrueKind, FalseKind" (e.g., "Star, StarOutline")
/// </summary>
public class BoolToMaterialIconKindConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b && parameter is string kinds)
        {
            var parts = kinds.Split(',');
            if (parts.Length == 2)
            {
                var kindName = b ? parts[0].Trim() : parts[1].Trim();
                if (Enum.TryParse<MaterialIconKind>(kindName, true, out var result))
                    return result;
            }
        }
        return MaterialIconKind.Help; // Fallback
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// Converts a string (from ViewModel) to a MaterialIconKind enum.
/// </summary>
public class StringToMaterialIconKindConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string str && !string.IsNullOrWhiteSpace(str))
        {
            if (Enum.TryParse<MaterialIconKind>(str, true, out var result))
                return result;
        }
        return MaterialIconKind.Help; // Fallback
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}