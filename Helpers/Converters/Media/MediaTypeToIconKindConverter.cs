using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Material.Icons;
using NullWave.Models;

namespace NullWave.Helpers.Converters;

public class MediaTypeToIconKindConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is MediaType mediaType)
        {
            return mediaType switch
            {
                MediaType.Radio => MaterialIconKind.Radio,
                MediaType.Audiobook => MaterialIconKind.BookMusic,
                MediaType.Podcast => MaterialIconKind.Podcast,
                _ => MaterialIconKind.MusicNote
            };
        }
        return MaterialIconKind.MusicNote;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}