using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace NullWave.Helpers.Converters;

public class StringEqualsMultiConverter : IMultiValueConverter
{
    public static readonly StringEqualsMultiConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count >= 2 && values[0] is string a && values[1] is string b)
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        return false;
    }
}