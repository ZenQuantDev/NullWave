using System;
using System.Collections.Generic;
using System.Linq;

namespace NullWave.Helpers;

/// <summary>
/// Pure logic for computing listening streaks. 
/// Handles UTC-to-Local conversion correctly so streaks don't break at midnight boundaries.
/// </summary>
public static class ListeningStreaks
{
    public static (int Current, int Longest) Compute(
        IEnumerable<DateTime> playedUtcDates, 
        DateTime nowUtc, 
        TimeZoneInfo? userZone = null)
    {
        userZone ??= TimeZoneInfo.Local;
        
        // Convert UTC play times to local dates for streak grouping
        DateTime ToLocalDate(DateTime utc) => 
            TimeZoneInfo.ConvertTimeFromUtc(UtcTime.AsUtc(utc), userZone).Date;

        var days = playedUtcDates.Select(ToLocalDate).Distinct().OrderBy(d => d).ToList();
        if (days.Count == 0) return (0, 0);

        // Calculate longest historical streak
        int longest = 1, run = 1;
        for (int i = 1; i < days.Count; i++)
        {
            run = days[i] == days[i - 1].AddDays(1) ? run + 1 : 1;
            longest = Math.Max(longest, run);
        }

        // Calculate current streak (survives until end of today)
        var daySet = days.ToHashSet();
        var cursor = ToLocalDate(nowUtc);
        
        // If no play today, check yesterday (streak survives until midnight)
        if (!daySet.Contains(cursor)) 
            cursor = cursor.AddDays(-1);
            
        int current = 0;
        while (daySet.Contains(cursor)) 
        { 
            current++; 
            cursor = cursor.AddDays(-1); 
        }

        return (current, longest);
    }
}