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
        var L = LocalizationService.Instance;
        return field switch
        {
            SortField.DateAdded  => L["Sort_DateAdded"],
            SortField.PlayCount  => L["Sort_PlayCount"],
            SortField.LastPlayed => L["Sort_LastPlayed"],
            SortField.Title      => L["Sort_Title"],
            SortField.Artist     => L["Sort_Artist"],
            SortField.Source     => L["Sort_Source"],
            _ => field.ToString()
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class BoolToSortIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // Returns the string name of the Material Icon, which Avalonia automatically parses into the MaterialIconKind enum
        if (value is bool ascending)
        {
            return ascending ? "SortAscending" : "SortDescending";
        }
        return "SortAscending";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}