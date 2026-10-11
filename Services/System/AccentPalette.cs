using System;
using Avalonia.Media;

namespace NullWave.Services;

public enum ThemeKind { Dark, TrueBlack, Light }

public record AccentPalette(
    Color Accent,
    Color AccentHover,
    Color AccentDim,
    Color AccentGlow,
    Color Accent2,
    Color Accent2Dim,
    Color Accent2Glow,
    Color TextOnAccent)
{
    public static AccentPalette Compute(Color primary, Color secondary, ThemeKind kind)
    {
        var light = kind == ThemeKind.Light;
        var mixTarget = kind switch
        {
            ThemeKind.Light => Colors.White,
            ThemeKind.TrueBlack => Colors.Black,
            _ => Color.Parse("#111827")
        };

        var textOnAccent = ContrastRatio(Colors.White, primary) >= ContrastRatio(Colors.Black, primary)
            ? Colors.White
            : Colors.Black;

        return new AccentPalette(
            Accent: primary,
            AccentHover: light ? Mix(primary, Colors.Black, 0.15) : Mix(primary, Colors.White, 0.22),
            AccentDim: Mix(primary, mixTarget, light ? 0.85 : 0.72),
            AccentGlow: Mix(primary, mixTarget, light ? 0.65 : 0.55),
            Accent2: light ? Mix(secondary, Colors.Black, 0.35) : secondary,
            Accent2Dim: Mix(secondary, mixTarget, light ? 0.85 : 0.72),
            Accent2Glow: Mix(secondary, mixTarget, light ? 0.65 : 0.55),
            TextOnAccent: textOnAccent);
    }

    internal static Color Mix(Color a, Color b, double t) => new Color(255,
        (byte)Math.Round(a.R + (b.R - a.R) * t),
        (byte)Math.Round(a.G + (b.G - a.G) * t),
        (byte)Math.Round(a.B + (b.B - a.B) * t));

    internal static double Luminance(Color color)
    {
        static double Channel(byte value)
        {
            var srgb = value / 255.0;
            return srgb <= 0.03928 ? srgb / 12.92 : Math.Pow((srgb + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }

    internal static double ContrastRatio(Color a, Color b)
    {
        var luminanceA = Luminance(a);
        var luminanceB = Luminance(b);
        var (higher, lower) = luminanceA >= luminanceB
            ? (luminanceA, luminanceB)
            : (luminanceB, luminanceA);
        return (higher + 0.05) / (lower + 0.05);
    }
}