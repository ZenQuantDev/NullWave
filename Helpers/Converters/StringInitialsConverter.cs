using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace NullWave.Helpers.Converters;

public class StringInitialsConverter : IValueConverter
{
    public static readonly StringInitialsConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string name || string.IsNullOrWhiteSpace(name))
            return "?";

        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return "?";
        
        var initials = words.Length >= 2 
            ? $"{words[0][0]}{words[1][0]}"
            : words[0][..Math.Min(2, words[0].Length)];
        
        return initials.ToUpperInvariant();
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}