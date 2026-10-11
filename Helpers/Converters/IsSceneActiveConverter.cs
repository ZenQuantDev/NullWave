using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace NullWave.Helpers.Converters;

public class IsSceneActiveConverter : IMultiValueConverter
{
    public static readonly IsSceneActiveConverter Instance = new();

    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count >= 3 && 
            values[0] is string style && 
            values[1] is string activeScene && 
            values[2] is string targetScene)
        {
            return string.Equals(style, "Scene", StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(activeScene, targetScene, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }
}