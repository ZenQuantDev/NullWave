using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace NullWave.Helpers.Converters;

/// <summary>
/// Compares two bound Guid values (typically a track row's Id against
/// Player.CurrentTrack.Id) and returns true if they're equal and both
/// non-null. Used to drive a "now playing" row highlight that persists
/// across different filtered views of the library — unlike
/// ListBoxItem:selected, which is local to a single ListBox and is lost
/// whenever Tracks is rebuilt by a filter change.
/// </summary>
public class TrackIdEqualsConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2) return false;
        if (values[0] is Guid a && values[1] is Guid b)
            return a == b;
        return false;
    }
}