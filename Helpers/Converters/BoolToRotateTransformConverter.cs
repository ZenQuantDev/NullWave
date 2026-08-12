using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace NullWave.Helpers.Converters;

/// <summary>true → 90° rotation (expanded chevron), false → 0°.</summary>
public class BoolToRotateTransformConverter : IValueConverter
{
    public static readonly BoolToRotateTransformConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => new RotateTransform(value is true ? 90 : 0);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}