using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace NullWave.Helpers.Converters;

/// <summary>
/// Multiplies a position value (0-1) by a pixel width to produce a fill width.
/// Used for the seek bar elapsed-fill overlay.
/// </summary>
public class PositionToWidthConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count != 2) return 0d;
        
        if (values[0] is double position && values[1] is double width)
        {
            return position * width;
        }
        
        return 0d;
    }
}