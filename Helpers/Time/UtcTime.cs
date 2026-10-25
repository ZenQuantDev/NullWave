using System;

namespace NullWave.Helpers;

/// <summary>
/// Centralized UTC handling to prevent DateTimeKind mismatches.
/// </summary>
public static class UtcTime
{
    /// <summary>
    /// Ensures a DateTime is treated as UTC. 
    /// Local times are converted; Unspecified times are assumed UTC.
    /// </summary>
    public static DateTime AsUtc(DateTime d) => d.Kind switch
    {
        DateTimeKind.Utc => d,
        DateTimeKind.Local => d.ToUniversalTime(),
        _ => DateTime.SpecifyKind(d, DateTimeKind.Utc)
    };

    public static DateTime? AsUtc(DateTime? d) => d.HasValue ? AsUtc(d.Value) : null;

    /// <summary>
    /// Generates a sortable, culture-invariant timestamp for filenames.
    /// </summary>
    public static string FileStamp() =>
        DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
}