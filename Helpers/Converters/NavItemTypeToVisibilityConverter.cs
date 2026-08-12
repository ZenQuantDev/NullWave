using System;
using System.Globalization;
using Avalonia.Data.Converters;
using NullWave.Models;

namespace NullWave.Helpers.Converters;

public class NavItemTypeToVisibilityConverter : IValueConverter
{
    public static readonly NavItemTypeToVisibilityConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not NavItemType type || parameter is not string param) return false;
        return param switch
        {
            "PinnedPlaylist" => type == NavItemType.PinnedPlaylist,
            _ => false
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}