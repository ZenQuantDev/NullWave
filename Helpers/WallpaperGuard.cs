using System;

namespace NullWave.Helpers;

internal static class WallpaperGuard
{
    public static int BlurForSoftness(int softness) => Math.Clamp(softness, 0, 5) * 10;

    public static int SoftnessForBlur(int blur) => Math.Clamp((blur + 5) / 10, 0, 5);

    public static bool ShouldSkipDecode(string? lastPath, int lastWidth, string? newPath, int newWidth)
    {
        if (string.IsNullOrEmpty(newPath)) return true;
        if (string.IsNullOrEmpty(lastPath)) return false;

        return string.Equals(lastPath, newPath, StringComparison.OrdinalIgnoreCase) && lastWidth == newWidth;
    }

    public static string NormalizeStyle(string style, string path, bool fileExists)
    {
        return string.Equals(style, "Custom", StringComparison.OrdinalIgnoreCase) && !fileExists
            ? "None"
            : style;
    }
}