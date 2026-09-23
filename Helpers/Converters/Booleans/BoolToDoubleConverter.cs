using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace NullWave.Helpers.Converters;

/// <summary>
/// Converts a bool to a double. Parameter format: "falseValue,trueValue" (e.g. "180,0"
/// for MinWidth: 180 when expanded/false, 0 when collapsed/true).
/// </summary>
public class BoolToDoubleConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not bool flag) return 0d;
        var parts = (parameter as string)?.Split(',') ?? new[] { "0", "0" };
        return double.Parse(flag ? parts[1] : parts[0], CultureInfo.InvariantCulture);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}