using System;

namespace NullWave.Helpers;

public enum PresetWallpaperAction { Keep, ApplyScene, TurnOff, Skip }

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
        if (string.Equals(style, "Custom", StringComparison.OrdinalIgnoreCase) && !fileExists) return "None";
        return IsKnownStyle(style) ? style : "None";
    }

    private static bool IsKnownStyle(string style) =>
        string.Equals(style, "None", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(style, "Custom", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(style, "AccentGlow", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(style, "Scene", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(style, "BuiltIn", StringComparison.OrdinalIgnoreCase);

    public static bool TrueBlackWarning(string themeMode, string style, bool sceneIsOledSafe)
    {
        if (!string.Equals(themeMode, "TrueBlack", StringComparison.OrdinalIgnoreCase)) return false;
        return style switch
        {
            "Custom" or "AccentGlow" => true,
            "Scene" => !sceneIsOledSafe,
            _ => false
        };
    }

    public static PresetWallpaperAction PresetWallpaperAction(string currentStyle, string? presetSceneId, bool minimalTier)
    {
        if (presetSceneId == null) return Helpers.PresetWallpaperAction.Keep;
        if (string.Equals(presetSceneId, "none", StringComparison.OrdinalIgnoreCase))
            return currentStyle is "Scene" or "AccentGlow"
                ? Helpers.PresetWallpaperAction.TurnOff
                : Helpers.PresetWallpaperAction.Keep;
        if (minimalTier) return Helpers.PresetWallpaperAction.Skip;
        return currentStyle is "None" or "AccentGlow" or "Scene"
            ? Helpers.PresetWallpaperAction.ApplyScene
            : Helpers.PresetWallpaperAction.Keep;
    }
}