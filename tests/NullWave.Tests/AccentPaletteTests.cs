using System.Linq;
using Avalonia.Media;
using NullWave.Services;
using Xunit;

namespace NullWave.Tests;

public class AccentPaletteTests
{
    private static System.Collections.Generic.IEnumerable<ThemeService.AccentDef> AllAccents =>
        ThemeService.BaseAccents
            .Concat(ThemeService.DuotoneAccents)
            .Concat(new[] { ThemeService.CodenameAccent });

    private static readonly ThemeKind[] Kinds = { ThemeKind.Dark, ThemeKind.TrueBlack, ThemeKind.Light };

    public static TheoryData<string, ThemeKind> AllAccentModePairs()
    {
        var data = new TheoryData<string, ThemeKind>();
        foreach (var accent in AllAccents)
            foreach (var kind in Kinds)
                data.Add(accent.Name, kind);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllAccentModePairs))]
    public void Text_on_accent_keeps_at_least_4_5_to_1_contrast(string accentName, ThemeKind kind)
    {
        var def = AllAccents.First(a => a.Name == accentName);
        var palette = AccentPalette.Compute(def.PrimaryColor, def.SecondaryColor, kind);
        var ratio = AccentPalette.ContrastRatio(palette.TextOnAccent, palette.Accent);
        Assert.True(ratio >= 4.5, $"{accentName} / {kind}: contrast {ratio:0.00}");
    }

    [Theory]
    [InlineData(ThemeKind.Dark)]
    [InlineData(ThemeKind.TrueBlack)]
    public void Glows_and_dims_are_darker_than_the_accent_in_dark_modes(ThemeKind kind)
    {
        foreach (var def in AllAccents)
        {
            var palette = AccentPalette.Compute(def.PrimaryColor, def.SecondaryColor, kind);
            Assert.True(AccentPalette.Luminance(palette.AccentGlow) < AccentPalette.Luminance(palette.Accent), $"{def.Name} glow");
            Assert.True(AccentPalette.Luminance(palette.AccentDim) < AccentPalette.Luminance(palette.AccentGlow), $"{def.Name} dim");
        }
    }

    [Fact]
    public void Glows_and_dims_are_lighter_than_the_accent_in_light_mode()
    {
        foreach (var def in AllAccents)
        {
            var palette = AccentPalette.Compute(def.PrimaryColor, def.SecondaryColor, ThemeKind.Light);
            Assert.True(AccentPalette.Luminance(palette.AccentGlow) > AccentPalette.Luminance(palette.Accent), $"{def.Name} glow");
            Assert.True(AccentPalette.Luminance(palette.AccentDim) > AccentPalette.Luminance(palette.AccentGlow), $"{def.Name} dim");
        }
    }

    [Fact]
    public void Accent2_tints_are_mixed_from_the_secondary_color()
    {
        var accent = ThemeService.CodenameAccent;
        var palette = AccentPalette.Compute(accent.PrimaryColor, accent.SecondaryColor, ThemeKind.Dark);
        Assert.Equal(AccentPalette.Mix(accent.SecondaryColor, Color.Parse("#111827"), 0.72), palette.Accent2Dim);
        Assert.Equal(AccentPalette.Mix(accent.SecondaryColor, Color.Parse("#111827"), 0.55), palette.Accent2Glow);
        Assert.Equal(accent.SecondaryColor, palette.Accent2);
    }

    [Fact]
    public void Text_choice_is_the_higher_contrast_candidate()
    {
        var mid = Color.Parse("#8B5CF6");
        var palette = AccentPalette.Compute(mid, mid, ThemeKind.Dark);
        var white = AccentPalette.ContrastRatio(Colors.White, mid);
        var black = AccentPalette.ContrastRatio(Colors.Black, mid);
        Assert.Equal(white >= black ? Colors.White : Colors.Black, palette.TextOnAccent);
    }
}