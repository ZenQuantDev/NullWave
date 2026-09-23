using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace NullWave.Helpers.Converters;

/// <summary>
/// Shows the live ICY "now playing" line only on the row that is both
/// a Radio track AND the currently playing track.
/// Values: [0] = row Track.Id, [1] = Player.CurrentTrack.Id, [2] = row IsRadio
/// </summary>
public class RadioNowPlayingConverter : IMultiValueConverter
{
    public static readonly RadioNowPlayingConverter Instance = new();

    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 3) return false;
        return values[0] is Guid id
            && values[1] is Guid currentId
            && values[2] is bool isRadio
            && isRadio
            && id == currentId;
    }
}