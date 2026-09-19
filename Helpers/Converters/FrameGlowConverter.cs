using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace NullWave.Helpers.Converters;

public class FrameGlowConverter : IValueConverter
{
    public static readonly FrameGlowConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value as string == "Glow")
        {
            var res = Application.Current?.Resources;
            var c1 = res?["ColorAccent"] is Color col1 ? col1 : Color.Parse("#4E9BFF");
            var c2 = res?["ColorAccent2"] is Color col2 ? col2 : Color.Parse("#B08CFF");

            // BoxShadow uses property initializer syntax, not positional constructor
            var shadow1 = new BoxShadow
            {
                OffsetX = 0,
                OffsetY = 0,
                Blur = 12,
                Spread = 2,
                Color = c1
            };
            var shadow2 = new BoxShadow
            {
                OffsetX = 0,
                OffsetY = 0,
                Blur = 24,
                Spread = 6,
                Color = Color.FromArgb(120, c2.R, c2.G, c2.B)
            };

            // BoxShadows constructor: (BoxShadow first, BoxShadow[] rest)
            return new BoxShadows(shadow1, new[] { shadow2 });
        }
        return new BoxShadows();
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}