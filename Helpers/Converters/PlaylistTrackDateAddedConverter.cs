using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using NullWave.Models;

namespace NullWave.Helpers.Converters;

public class PlaylistTrackDateAddedConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count == 2 && values[0] is Track track && values[1] is Playlist playlist)
            return playlist.GetDateAdded(track.Id);
        return null;
    }
}