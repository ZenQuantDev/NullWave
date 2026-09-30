using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia;
using Avalonia.Media;
using Material.Icons;
using NullWave.Models;

namespace NullWave.Helpers;

/// <summary>
/// Parse-once vector geometries for track-row icons, size-baked.
/// Material.Icons path data lives in a 24x24 viewbox; Geometry.Parse returns TIGHT
/// bounds, so Stretch="Uniform" over-scales narrow glyphs (dots, music note) to fill
/// the box. We bake the 24->size scale into the geometry and render with
/// Stretch="None", reproducing MaterialIcon's exact metrics with no TemplatedControl.
/// </summary>
public static class RowIcons
{
    private static readonly Dictionary<(MaterialIconKind, int), Geometry> Cache = new();

    private static Geometry Get(MaterialIconKind kind, int size)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue((kind, size), out var g)) return g;
            g = Geometry.Parse(MaterialIconDataProvider.GetData(kind));
            var s = size / 24.0;
            g.Transform = new MatrixTransform(new Matrix(s, 0, 0, s, 0, 0));
            Cache[(kind, size)] = g;
            return g;
        }
    }

    public static Geometry Star18         => Get(MaterialIconKind.Star, 18);
    public static Geometry StarOutline18  => Get(MaterialIconKind.StarOutline, 18);
    public static Geometry MusicNote18    => Get(MaterialIconKind.MusicNote, 18);
    public static Geometry Radio18        => Get(MaterialIconKind.Radio, 18);
    public static Geometry BookOpen18     => Get(MaterialIconKind.BookOpen, 18);
    public static Geometry Podcast18      => Get(MaterialIconKind.Podcast, 18);
    public static Geometry CheckCircle16  => Get(MaterialIconKind.CheckCircle, 16);
    public static Geometry CloudOff16     => Get(MaterialIconKind.CloudOff, 16);
    public static Geometry AccessPoint16  => Get(MaterialIconKind.AccessPoint, 16);
    public static Geometry DotsVertical18 => Get(MaterialIconKind.DotsVertical, 18);
}

public class MediaTypeToGeometryConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is MediaType mt ? mt switch
    {
        MediaType.Radio     => RowIcons.Radio18,
        MediaType.Audiobook => RowIcons.BookOpen18,
        MediaType.Podcast   => RowIcons.Podcast18,
        _                   => RowIcons.MusicNote18,
    } : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class BoolToStarGeometryConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? RowIcons.Star18 : RowIcons.StarOutline18;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}