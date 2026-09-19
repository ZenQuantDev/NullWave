using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace NullWave.Helpers.Converters;

/// <summary>True = show plaintext ('\0' disables masking), False = mask with ●.</summary>
public class BoolToPasswordCharConverter : IValueConverter
{
    public static readonly BoolToPasswordCharConverter Instance = new();
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? '\0' : '●';
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}