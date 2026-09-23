using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace NullWave.Helpers.Converters;

/// <summary>
/// Returns "playlist-row active" if the two Guids match, otherwise "playlist-row".
/// Used to highlight the currently selected playlist row.
/// </summary>
public class SelectedPlaylistClassConverter : IMultiValueConverter
{
    public static readonly SelectedPlaylistClassConverter Instance = new();

    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count != 2) return "playlist-row";
        
        var selectedId = values[0] as Guid?;
        var rowId = values[1] as Guid?;
        
        if (selectedId.HasValue && rowId.HasValue && selectedId.Value == rowId.Value)
            return "playlist-row active";
        
        return "playlist-row";
    }
}