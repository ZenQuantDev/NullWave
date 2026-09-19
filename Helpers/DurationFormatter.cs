using System;

namespace NullWave.Helpers;

public static class DurationFormatter
{
    public static string Format(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero) return "-";
        int totalHours = (int)duration.TotalHours;
        return totalHours >= 1
            ? $"{totalHours}:{duration:mm\\:ss}"
            : duration.ToString(@"mm\:ss");
    }

    public static string FormatListeningTime(TimeSpan total)
    {
        if (total.TotalHours >= 1) return $"{(int)total.TotalHours}h {total.Minutes}m";
        if (total.TotalMinutes >= 1) return $"{(int)total.TotalMinutes}m";
        return "<1m";
    }
}