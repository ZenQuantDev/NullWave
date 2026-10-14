using System;
using System.Globalization;
using Avalonia.Data.Converters;
using NullWave.Services;

namespace NullWave.Helpers.Converters;

/// <summary>Converts a DateTime to a localized relative timestamp ("2h ago").</summary>
public class RelativeTimeConverter : IValueConverter
{
    public static readonly RelativeTimeConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is DateTime dt)
        {
            var L = LocalizationService.Instance;
            
            // D9 FIX: Standardize on UTC. If the DateTime is Local, convert it to UTC first.
            if (dt.Kind == DateTimeKind.Local) dt = dt.ToUniversalTime();
            var span = DateTime.UtcNow - dt;

            if (span.TotalMinutes < 1) return L["Profile_Time_Now"];
            if (span.TotalMinutes < 60) return string.Format(L["Profile_Time_MinutesAgo"], (int)span.TotalMinutes);
            if (span.TotalHours < 24) return string.Format(L["Profile_Time_HoursAgo"], (int)span.TotalHours);
            if (span.TotalDays < 30) return string.Format(L["Profile_Time_DaysAgo"], (int)span.TotalDays);
            
            // Fallback: display the absolute date in the user's local time zone
            return dt.ToLocalTime().ToString("MMM dd, yyyy", culture);
        }
        return null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}