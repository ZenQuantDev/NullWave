using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace NullWave.Helpers.Converters;

/// <summary>Picks one of two brushes from a bool. Theme-aware because the
/// brushes themselves are resolved from ThemeDictionaries at runtime.</summary>
public class BoolToBrushConverter : IValueConverter
{
    public IBrush? TrueBrush { get; set; }
    public IBrush? FalseBrush { get; set; }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? TrueBrush : FalseBrush;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is IBrush b && ReferenceEquals(b, TrueBrush);
}
