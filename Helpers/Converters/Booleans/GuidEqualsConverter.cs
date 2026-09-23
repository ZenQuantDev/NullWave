using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace NullWave.Helpers.Converters;

public class GuidEqualsConverter : IMultiValueConverter
{
    public static readonly GuidEqualsConverter Instance = new();

    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count != 2) return false;
        if (values[0] is Guid a && values[1] is Guid b) return a == b;
        return false;
    }
}
