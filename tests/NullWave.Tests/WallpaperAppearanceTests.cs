using Avalonia.Media;
using NullWave.Helpers;
using NullWave.Helpers.Localization;
using NullWave.Models;
using NullWave.Services;

namespace NullWave.Tests;

[Collection("Database")]
public class WallpaperAppearanceTests : IDisposable
{
    public void Dispose() => WallpaperService.Instance.ApplyFrom(new Preferences());

    [Theory]
    [InlineData("Fill", Stretch.UniformToFill)]
    [InlineData("Fit", Stretch.Uniform)]
    [InlineData("Stretch", Stretch.Fill)]
    [InlineData("Unknown", Stretch.UniformToFill)]
    public void Wallpaper_fit_maps_to_expected_stretch(string fit, Stretch expected)
    {
        WallpaperService.Instance.ApplyFrom(new Preferences { WallpaperFit = fit });

        Assert.Equal(expected, WallpaperService.Instance.Stretch);
    }

    [Theory]
    [InlineData(0, 1920)]
    [InlineData(10, 960)]
    [InlineData(30, 480)]
    [InlineData(50, 320)]
    [InlineData(500, 160)]
    public void Blur_maps_to_baked_decode_width(int blur, int expectedWidth)
        => Assert.Equal(expectedWidth, WallpaperService.DecodeWidthForBlur(blur));

    [Fact]
    public void Wallpaper_guard_skips_decode_only_for_same_nonempty_path_and_width()
    {
        Assert.True(WallpaperGuard.ShouldSkipDecode("C:\\img.webp", 1920, "C:\\img.webp", 1920));
        Assert.True(WallpaperGuard.ShouldSkipDecode("C:\\IMG.webp", 1920, "C:\\img.webp", 1920));
        Assert.False(WallpaperGuard.ShouldSkipDecode("C:\\old.webp", 1920, "C:\\new.webp", 1920));
        Assert.False(WallpaperGuard.ShouldSkipDecode("C:\\img.webp", 1920, "C:\\img.webp", 960));
        Assert.False(WallpaperGuard.ShouldSkipDecode(null, 0, "C:\\img.webp", 1920));
        Assert.True(WallpaperGuard.ShouldSkipDecode("C:\\img.webp", 1920, "", 1920));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 10)]
    [InlineData(3, 30)]
    [InlineData(5, 50)]
    [InlineData(-1, 0)]
    [InlineData(8, 50)]
    public void Wallpaper_softness_steps_preserve_the_legacy_blur_scale(int softness, int expectedBlur)
    {
        Assert.Equal(expectedBlur, WallpaperGuard.BlurForSoftness(softness));
        Assert.InRange(WallpaperGuard.SoftnessForBlur(expectedBlur), 0, 5);
    }

    [Fact]
    public void Missing_custom_wallpaper_normalizes_to_none()
    {
        Assert.Equal("None", WallpaperGuard.NormalizeStyle("Custom", "C:\\missing.webp", fileExists: false));
        Assert.Equal("Custom", WallpaperGuard.NormalizeStyle("Custom", "C:\\exists.webp", fileExists: true));
        Assert.Equal("AccentGlow", WallpaperGuard.NormalizeStyle("AccentGlow", "", fileExists: false));
    }

    [Fact]
    public void Appearance_translations_have_english_and_russian_entries()
    {
        var keys = new[]
        {
            "Settings_Appearance_Section_Wallpaper",
            "Settings_Appearance_Section_Presets",
            "Settings_Appearance_Reset",
            "Settings_Appearance_Wallpaper_Browse",
            "Settings_Appearance_Wallpaper_Off",
            "Settings_Appearance_Wallpaper_ActiveFmt",
            "Settings_Appearance_Wallpaper_Source_Title",
            "Settings_Appearance_Wallpaper_Mode_None",
            "Settings_Appearance_Wallpaper_Mode_Custom",
            "Settings_Appearance_Wallpaper_Mode_AccentGlow",
            "Settings_Appearance_Wallpaper_Mode_AlbumArt",
            "Settings_Appearance_Wallpaper_Opacity",
            "Settings_Appearance_Wallpaper_OpacityTooltip",
            "Settings_Appearance_Wallpaper_Blur",
            "Settings_Appearance_Wallpaper_Blur_Desc",
            "Settings_Appearance_Wallpaper_Missing",
            "Settings_Appearance_Wallpaper_AlbumArt_Soon",
            "Settings_Appearance_Wallpaper_Fit",
            "Settings_Appearance_Wallpaper_Set",
            "Settings_Appearance_Wallpaper_TrueBlackWarn",
            "Settings_Appearance_Theme_Dark_Desc",
            "Settings_Appearance_Theme_TrueBlack_Desc",
            "Settings_Appearance_Theme_Light_Desc",
            "Settings_Appearance_Theme_System_Desc",
            "Settings_Appearance_Preset_OxeyeClassic",
            "Settings_Appearance_Preset_OxeyeClassic_Desc",
            "Settings_Appearance_Preset_MidnightOled",
            "Settings_Appearance_Preset_MidnightOled_Desc",
            "Settings_Appearance_Preset_StudioLight",
            "Settings_Appearance_Preset_StudioLight_Desc",
            "Settings_Appearance_Preset_FocusMinimal",
            "Settings_Appearance_Preset_FocusMinimal_Desc",
        };

        Assert.All(keys, key =>
        {
            Assert.True(Locales.English.ContainsKey(key), $"Missing English key: {key}");
            Assert.True(Locales.Russian.ContainsKey(key), $"Missing Russian key: {key}");
        });
    }

    [Fact]
    public void Appearance_presets_have_unique_ids()
    {
        Assert.Equal(4, ThemeService.AppearancePresets.Count);
        Assert.Equal(ThemeService.AppearancePresets.Count, ThemeService.AppearancePresets.Select(preset => preset.Id).Distinct().Count());
    }

    [Theory]
    [InlineData("None", "", false)]
    [InlineData("AccentGlow", "", true)]
    [InlineData("Custom", "missing-wallpaper.png", false)]
    public void Active_wallpaper_requires_a_renderable_mode(string style, string path, bool expected)
    {
        var wallpaperPath = path == "missing-wallpaper.png"
            ? Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".png")
            : path;

        WallpaperService.Instance.ApplyFrom(new Preferences
        {
            WallpaperStyle = style,
            WallpaperPath = wallpaperPath,
        });

        Assert.Equal(expected, WallpaperService.Instance.HasActiveWallpaper);
    }

    [Fact]
    public void Missing_custom_wallpaper_is_normalized_to_none()
    {
        WallpaperService.Instance.ApplyFrom(new Preferences
        {
            WallpaperStyle = "Custom",
            WallpaperPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".png"),
        });

        Assert.Equal("None", WallpaperService.Instance.Style);
        Assert.False(WallpaperService.Instance.HasActiveWallpaper);
    }

    [Fact]
    public void Custom_wallpaper_source_keeps_chrome_active_before_decode_completes()
    {
        var path = Path.Combine(Path.GetTempPath(), "nw-wall-" + Guid.NewGuid().ToString("N") + ".png");
        File.WriteAllBytes(path, new byte[] { 1 });
        try
        {
            WallpaperService.Instance.ApplyFrom(new Preferences { WallpaperStyle = "Custom", WallpaperPath = path });

            Assert.True(WallpaperService.Instance.HasActiveWallpaper);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Reserve_cache_path_does_not_return_an_existing_file()
    {
        var first = WallpaperService.ReserveCachePath(".jpg");
        File.WriteAllText(first, "occupied");
        try
        {
            var second = WallpaperService.ReserveCachePath(".jpg");

            Assert.NotEqual(first, second);
        }
        finally
        {
            File.Delete(first);
        }
    }

    [Fact]
    public void Prune_wallpaper_cache_keeps_only_the_active_wallpaper()
    {
        var keep = WallpaperService.ReserveCachePath(".png");
        var legacy = Path.Combine(NullWavePaths.ArtCacheDir, "wallpaper.jpg");
        var old = Path.Combine(NullWavePaths.ArtCacheDir, "wallpaper-1.png");
        var unrelated = Path.Combine(NullWavePaths.ArtCacheDir, "yt_cache.jpg");
        File.WriteAllText(keep, "keep");
        File.WriteAllText(legacy, "old");
        File.WriteAllText(old, "old");
        File.WriteAllText(unrelated, "row art");
        try
        {
            WallpaperService.PruneWallpaperCache(keep);

            Assert.True(File.Exists(keep));
            Assert.False(File.Exists(legacy));
            Assert.False(File.Exists(old));
            Assert.True(File.Exists(unrelated));
        }
        finally
        {
            WallpaperService.PruneWallpaperCache(string.Empty);
        }
    }
}