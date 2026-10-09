using System;

namespace NullWave.Helpers;

internal static class ThemeGuard
{
    public static bool ShouldRemixAccent(
        string? appliedName,
        string? appliedMode,
        string requestedName,
        string requestedMode,
        bool paletteWasRewritten)
    {
        if (paletteWasRewritten) return true;
        if (string.IsNullOrEmpty(appliedName) || string.IsNullOrEmpty(appliedMode)) return true;

        return !string.Equals(appliedName, requestedName, StringComparison.OrdinalIgnoreCase) ||
               !string.Equals(appliedMode, requestedMode, StringComparison.OrdinalIgnoreCase);
    }
}