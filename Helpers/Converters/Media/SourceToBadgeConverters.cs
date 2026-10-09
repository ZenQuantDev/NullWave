using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using NullWave.Models;

namespace NullWave.Helpers.Converters;

/// <summary>Pure brand-tint table; unit-testable without a UI thread and with no
/// resource lookup (the old lookup returned unset brushes at row-bind time, which
/// rendered the SOURCE column invisible). Light/dark is an INPUT, so the MultiBinding
/// re-evaluates on every theme flip.</summary>
public static class BadgeColors
{
    public static (Color Bg, Color Fg) For(TrackSource source, bool light) => source switch
    {
        TrackSource.YouTube    => light ? (Parse("#1ACC0000"), Parse("#B30000")) : (Parse("#29CC0000"), Parse("#FF8A8A")),
        TrackSource.SoundCloud => light ? (Parse("#1AE85A00"), Parse("#B34700")) : (Parse("#29E85A00"), Parse("#FFB380")),
        TrackSource.Spotify    => light ? (Parse("#1A1DB954"), Parse("#0E6B30")) : (Parse("#291DB954"), Parse("#7BE0A2")),
        TrackSource.LastFm     => light ? (Parse("#1AD51007"), Parse("#A00C05")) : (Parse("#29D51007"), Parse("#FF7A72")),
        TrackSource.Local      => light ? (Parse("#1A4E9BFF"), Parse("#1A5276")) : (Parse("#294E9BFF"), Parse("#8BB8FF")),
        _                      => light ? (Parse("#1A6B7280"), Parse("#374151")) : (Parse("#296B7280"), Parse("#A8B4CC")),
    };

    /// <summary>Rows bind the enum; the inspector binds a display string. Accept both.</summary>
    public static TrackSource Coerce(object? value) => value switch
    {
        TrackSource s => s,
        string text => Enum.TryParse<TrackSource>(text, true, out var parsed) ? parsed : TrackSource.Unknown,
        _ => TrackSource.Unknown,
    };

    private static Color Parse(string hex) => Color.Parse(hex);
}

/// <summary>values[0] = Source (enum or string), values[1] = ThemeService.IsLightTheme.
/// ConverterParameter = "Bg" or "Fg". Always returns a constructed solid brush.</summary>
public class SourceBadgeBrushConverter : IMultiValueConverter
{
    public static readonly SourceBadgeBrushConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var light = values.Count > 1 && values[1] is bool b && b;
        var pair = BadgeColors.For(BadgeColors.Coerce(values.Count > 0 ? values[0] : null), light);
        var asFg = parameter is string p && p.Equals("Fg", StringComparison.OrdinalIgnoreCase);
        return new SolidColorBrush(asFg ? pair.Fg : pair.Bg);
    }
}