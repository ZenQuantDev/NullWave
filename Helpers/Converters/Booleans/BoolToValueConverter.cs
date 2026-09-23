using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace NullWave.Helpers.Converters;

/// <summary>
/// A generic converter to map a boolean to any two values, reducing the need for multiple specialized bool converters.
/// </summary>
public class BoolToValueConverter<T> : IValueConverter
{
    public T? TrueValue { get; set; }
    public T? FalseValue { get; set; }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && b ? TrueValue : FalseValue;
        
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) 
        => throw new NotSupportedException();
}