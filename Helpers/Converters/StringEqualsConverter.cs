using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace NullWave.Helpers.Converters;

public class StringEqualsConverter : IValueConverter, IMultiValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string str && parameter is string param)
            return str.Equals(param, StringComparison.OrdinalIgnoreCase);
        return false;
    }

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count == 2 && values[0] is string a && values[1] is string b)
            return a.Equals(b, StringComparison.OrdinalIgnoreCase);
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}