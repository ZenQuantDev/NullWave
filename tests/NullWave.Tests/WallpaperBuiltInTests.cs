using System.Linq;
using System.Text.Json;
using NullWave.Helpers;
using NullWave.Helpers.Localization;
using NullWave.Models;
using NullWave.Services;
using Xunit;

namespace NullWave.Tests;

public class WallpaperBuiltInRegistryTests
{
    [Fact]
    public void BuiltIn_ids_are_unique_and_lowercase()
    {
        Assert.Equal(WallpaperBuiltIns.All.Count, WallpaperBuiltIns.All.Select(definition => definition.Id).Distinct().Count());
        Assert.All(WallpaperBuiltIns.All, definition => Assert.Equal(definition.Id, definition.Id.ToLowerInvariant()));
    }

    [Fact]
    public void Every_built_in_has_english_and_russian_name_and_description()
    {
        foreach (var definition in WallpaperBuiltIns.All)
        {
            Assert.True(Locales.English.ContainsKey(definition.NameKey), $"EN {definition.NameKey}");
            Assert.True(Locales.Russian.ContainsKey(definition.NameKey), $"RU {definition.NameKey}");
            Assert.True(Locales.English.ContainsKey(definition.DescriptionKey), $"EN {definition.DescriptionKey}");
            Assert.True(Locales.Russian.ContainsKey(definition.DescriptionKey), $"RU {definition.DescriptionKey}");
        }
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("")]
    [InlineData(null)]
    public void Find_returns_null_for_unknown_or_empty(string? id)
        => Assert.Null(WallpaperBuiltIns.Find(id));

    [Fact]
    public void Starry_night_asset_is_an_embedded_jpeg()
    {
        var definition = WallpaperBuiltIns.StarryNight;
        Assert.StartsWith("avares://NullWave/Assets/", definition.AssetPath);
        Assert.EndsWith(".jpg", definition.AssetPath);
    }

    [Fact]
    public void Exclusive_requires_an_unlock_entry()
    {
        Assert.False(WallpaperBuiltIns.IsUnlocked(WallpaperBuiltIns.StarryNight, Enumerable.Empty<string>()));
        Assert.True(WallpaperBuiltIns.IsUnlocked(WallpaperBuiltIns.StarryNight, new[] { "starrynight" }));
    }

    [Theory]
    [InlineData(6, false, false, false)]
    [InlineData(7, true, false, false)]
    [InlineData(14, true, true, false)]
    [InlineData(21, true, false, true)]
    [InlineData(28, true, false, false)]
    public void Tap_ladder_rewards_match_the_easter_egg(int taps, bool lore, bool accent, bool unlock)
    {
        var reward = WallpaperBuiltIns.RewardFor(taps);
        Assert.Equal(lore, reward.Lore);
        Assert.Equal(accent, reward.SignatureAccent);
        Assert.Equal(unlock, reward.ExclusiveUnlock);
    }
}

[Collection("Database")]
public class WallpaperBuiltInServiceTests : IDisposable
{
    public void Dispose() => WallpaperService.Instance.ApplyFrom(new Preferences());

    [Fact]
    public void Locked_exclusive_normalizes_to_none()
    {
        var preferences = new Preferences { WallpaperStyle = "BuiltIn", WallpaperBuiltInId = "starrynight" };
        WallpaperService.Instance.ApplyFrom(preferences);
        Assert.Equal("None", preferences.WallpaperStyle);
        Assert.False(WallpaperService.Instance.HasActiveWallpaper);
    }

    [Fact]
    public void Unlocked_exclusive_is_active_before_decode_lands()
    {
        var preferences = new Preferences
        {
            WallpaperStyle = "BuiltIn",
            WallpaperBuiltInId = "starrynight",
            UnlockedExclusiveWallpapers = { "starrynight" }
        };
        WallpaperService.Instance.ApplyFrom(preferences);
        Assert.Equal("BuiltIn", WallpaperService.Instance.Style);
        Assert.True(WallpaperService.Instance.HasActiveWallpaper);
        Assert.False(WallpaperService.Instance.ShowImage);
    }

    [Fact]
    public void Unknown_built_in_id_normalizes_to_none()
    {
        var preferences = new Preferences { WallpaperStyle = "BuiltIn", WallpaperBuiltInId = "monalisa" };
        WallpaperService.Instance.ApplyFrom(preferences);
        Assert.Equal("None", preferences.WallpaperStyle);
    }

    [Fact]
    public void Old_prefs_json_without_builtin_fields_loads_defaults()
    {
        var preferences = JsonSerializer.Deserialize<Preferences>("{\"WallpaperStyle\":\"Scene\"}")!;
        Assert.Equal(string.Empty, preferences.WallpaperBuiltInId);
        Assert.Empty(preferences.UnlockedExclusiveWallpapers);
    }

    [Fact]
    public void Presets_never_touch_a_builtin_wallpaper()
        => Assert.Equal(PresetWallpaperAction.Keep,
            WallpaperGuard.PresetWallpaperAction("BuiltIn", "spotlight", minimalTier: false));
}