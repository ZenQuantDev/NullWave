using System;
using System.Globalization;
using Avalonia.Data.Converters;
using NullWave.Services;

namespace NullWave.Helpers.Converters;

public class SortFieldDisplayConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not SortField field) return value;
        return field switch
        {
            SortField.DateAdded  => "Date Added",
            SortField.PlayCount  => "Play Count",
            SortField.LastPlayed => "Last Played",
            _ => field.ToString() // Title, Artist, Source are already fine as-is
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class BoolToSortIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool ascending ? (ascending ? "SortAscending" : "SortDescending") : "SortAscending";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}