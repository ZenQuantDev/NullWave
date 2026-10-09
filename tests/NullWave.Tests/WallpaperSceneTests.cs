using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using NullWave.Helpers;
using NullWave.Helpers.Localization;
using NullWave.Models;
using NullWave.Services;
using Xunit;

namespace NullWave.Tests;

public class WallpaperSceneRegistryTests
{
    [Fact]
    public void Scene_ids_are_unique_and_lowercase()
    {
        Assert.Equal(WallpaperScenes.All.Count, WallpaperScenes.All.Select(scene => scene.Id).Distinct().Count());
        Assert.All(WallpaperScenes.All, scene => Assert.Equal(scene.Id, scene.Id.ToLowerInvariant()));
    }

    [Fact]
    public void Every_scene_has_english_and_russian_name_and_description()
    {
        foreach (var scene in WallpaperScenes.All)
        {
            Assert.True(Locales.English.ContainsKey(scene.NameKey), $"EN {scene.NameKey}");
            Assert.True(Locales.Russian.ContainsKey(scene.NameKey), $"RU {scene.NameKey}");
            Assert.True(Locales.English.ContainsKey(scene.DescriptionKey), $"EN {scene.DescriptionKey}");
            Assert.True(Locales.Russian.ContainsKey(scene.DescriptionKey), $"RU {scene.DescriptionKey}");
        }
    }

    [Theory]
    [InlineData("AURORA")]
    [InlineData("dusk")]
    public void Find_is_case_insensitive_for_known_ids(string id)
        => Assert.NotNull(WallpaperScenes.Find(id));

    [Theory]
    [InlineData("nope")]
    [InlineData("")]
    [InlineData(null)]
    public void Find_returns_null_for_unknown_or_empty(string? id)
        => Assert.Null(WallpaperScenes.Find(id));

    [Fact]
    public void Default_id_exists_in_registry()
        => Assert.NotNull(WallpaperScenes.Find(WallpaperScenes.DefaultId));

    [Fact]
    public void Scene_labels_notify_when_language_changes()
    {
        var scene = WallpaperScenes.All[0];
        var originalLanguage = LocalizationService.Instance.CurrentLanguage;
        var nameChanged = false;
        var descriptionChanged = false;
        scene.PropertyChanged += (_, args) =>
        {
            nameChanged |= args.PropertyName == nameof(scene.Name);
            descriptionChanged |= args.PropertyName == nameof(scene.Description);
        };

        try
        {
            LocalizationService.Instance.SetLanguage("ru-RU");
            Assert.True(nameChanged);
            Assert.True(descriptionChanged);
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(originalLanguage);
        }
    }

    [Fact]
    public void New_preferences_default_to_the_registry_default_scene()
        => Assert.Equal(WallpaperScenes.DefaultId, new Preferences().WallpaperSceneId);

    [Fact]
    public void Old_prefs_json_without_scene_id_loads_with_the_default()
    {
        var prefs = JsonSerializer.Deserialize<Preferences>("{\"WallpaperStyle\":\"Scene\"}")!;
        Assert.Equal(WallpaperScenes.DefaultId, prefs.WallpaperSceneId);
    }

    [Fact]
    public void Guard_normalizes_unknown_styles_to_none()
    {
        Assert.Equal("None", WallpaperGuard.NormalizeStyle("Bogus", "", fileExists: true));
        Assert.Equal("Scene", WallpaperGuard.NormalizeStyle("Scene", "", fileExists: false));
    }
}

[Collection("Database")]
public class WallpaperSceneServiceTests : IDisposable
{
    public void Dispose() => WallpaperService.Instance.ApplyFrom(new Preferences());

    [Fact]
    public void Minimal_tier_hides_full_scene_layers()
    {
        var service = WallpaperService.Instance;
        try
        {
            service.MinimalTier = true;
            Assert.False(service.ShowFullScenes);
            service.MinimalTier = false;
            Assert.True(service.ShowFullScenes);
        }
        finally
        {
            service.MinimalTier = false;
        }
    }

    [Fact]
    public void Scene_with_known_id_is_active_without_an_image()
    {
        WallpaperService.Instance.ApplyFrom(new Preferences { WallpaperStyle = "Scene", WallpaperSceneId = "aurora" });
        Assert.True(WallpaperService.Instance.ShowScene);
        Assert.False(WallpaperService.Instance.ShowImage);
        Assert.True(WallpaperService.Instance.HasActiveWallpaper);
    }

    [Theory]
    [InlineData("Scene", "nope")]
    [InlineData("Bogus", "aurora")]
    public void Unknown_scene_or_style_normalizes_to_none(string style, string sceneId)
    {
        var prefs = new Preferences { WallpaperStyle = style, WallpaperSceneId = sceneId };
        WallpaperService.Instance.ApplyFrom(prefs);
        Assert.Equal("None", prefs.WallpaperStyle);
        Assert.Equal("None", WallpaperService.Instance.Style);
        Assert.False(WallpaperService.Instance.HasActiveWallpaper);
    }

    [Fact]
    public void Switching_from_custom_to_scene_clears_the_image_synchronously()
    {
        var path = Path.Combine(Path.GetTempPath(), "nw-scene-" + Guid.NewGuid().ToString("N") + ".png");
        File.WriteAllBytes(path, new byte[] { 1 });
        try
        {
            WallpaperService.Instance.ApplyFrom(new Preferences { WallpaperStyle = "Custom", WallpaperPath = path });
            Assert.True(WallpaperService.Instance.HasActiveWallpaper);

            WallpaperService.Instance.ApplyFrom(new Preferences { WallpaperStyle = "Scene", WallpaperSceneId = "dusk" });
            Assert.Null(WallpaperService.Instance.Image);
            Assert.True(WallpaperService.Instance.ShowScene);
            Assert.False(WallpaperService.Instance.ShowImage);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Scene_to_scene_switch_keeps_the_image_null()
    {
        WallpaperService.Instance.ApplyFrom(new Preferences { WallpaperStyle = "Scene", WallpaperSceneId = "aurora" });
        WallpaperService.Instance.ApplyFrom(new Preferences { WallpaperStyle = "Scene", WallpaperSceneId = "spotlight" });
        Assert.Null(WallpaperService.Instance.Image);
        Assert.True(WallpaperService.Instance.ShowScene);
    }

    [Fact]
    public void Legacy_glow_is_unchanged_by_scenes()
    {
        WallpaperService.Instance.ApplyFrom(new Preferences { WallpaperStyle = "AccentGlow" });
        Assert.True(WallpaperService.Instance.ShowGlow);
        Assert.False(WallpaperService.Instance.ShowScene);
        Assert.True(WallpaperService.Instance.HasActiveWallpaper);
    }
}