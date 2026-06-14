using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using NullWave.Models;

namespace NullWave.Helpers.Converters;

public class SourceToBackgroundConverter : IValueConverter
{
    public static readonly SourceToBackgroundConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var source = value switch
        {
            TrackSource ts => ts.ToString(),
            string s       => s,
            _              => string.Empty
        };

        return source switch
        {
            "YouTube"    => Color.Parse("#CC0000"),
            "SoundCloud" => Color.Parse("#E85A00"),
            "Spotify"    => Color.Parse("#1A7A40"),
            "LastFm"     => Color.Parse("#8B0000"),
            "Local"      => Color.Parse("#1A5276"),
            _            => Color.Parse("#2D3A4A")
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}