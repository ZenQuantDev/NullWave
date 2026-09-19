using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace NullWave.Helpers.Converters;

/// <summary>
/// Computes the profile identity block margin so the avatar overlaps the banner
/// by exactly half its height, derived from design tokens (no magic pixels).
/// Inputs: [0] = ProfileBannerHeight, [1] = ProfileAvatarSize.
/// Output: Thickness(20, banner - avatar/2, 20, 20).
/// </summary>
public class ProfileIdentityMarginConverter : IMultiValueConverter
{
    public static readonly ProfileIdentityMarginConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        double banner = values.Count > 0 && values[0] is double b ? b : 120d;
        double avatar = values.Count > 1 && values[1] is double a ? a : 100d;
        return new Thickness(20, Math.Max(0, banner - avatar / 2), 20, 20);
    }
}