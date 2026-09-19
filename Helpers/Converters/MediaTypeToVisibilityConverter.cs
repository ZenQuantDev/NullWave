using System;
using System.Globalization;
using Avalonia.Data.Converters;
using NullWave.Models;

namespace NullWave.Helpers.Converters;

public class MediaTypeToVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is MediaType mediaType && parameter is string param)
        {
            if (Enum.TryParse<MediaType>(param, out var target))
                return mediaType == target;
        }
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}