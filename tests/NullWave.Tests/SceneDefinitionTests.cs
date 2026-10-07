using System.Text.RegularExpressions;
using NullWave.Services;
using NullWave.Tests.Support;

namespace NullWave.Tests.Wallpaper;

public class SceneDefinitionTests
{
    private static readonly Regex LayerScene = new(
        @"Path=SceneId,\s*Converter=\{StaticResource StrEq\},\s*ConverterParameter=(?<id>[A-Za-z0-9_]+)\}",
        RegexOptions.Compiled);

    private static readonly Regex GalleryPreview = new(
        @"Binding Id,\s*Converter=\{StaticResource StrEq\},\s*ConverterParameter=(?<id>[A-Za-z0-9_]+)\}""\s*>\s*<Border\.Background>(?<brush>.*?)</Border\.Background>",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static string LayerXaml => File.ReadAllText(RepoFiles.Find("WallpaperLayer.axaml"));
    private static string GalleryXaml => File.ReadAllText(RepoFiles.Find("AppearanceWallpaperView.axaml"));
    private static string[] RegistryIds => WallpaperScenes.All.Select(scene => scene.Id).ToArray();

    [Fact]
    public void Scene_ids_are_lowercase_unique_and_use_the_standard_localization_keys()
    {
        var ids = RegistryIds;
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());

        foreach (var scene in WallpaperScenes.All)
        {
            Assert.Matches("^[a-z][a-z0-9]*$", scene.Id);
            Assert.Equal($"Scene_{scene.Id}", scene.NameKey);
            Assert.Equal($"Scene_{scene.Id}_Desc", scene.DescriptionKey);
        }

        Assert.NotNull(WallpaperScenes.Find(WallpaperScenes.DefaultId));
    }

    [Fact]
    public void Every_scene_has_a_panel_in_the_layer_and_the_layer_has_no_unknown_scenes()
    {
        var inLayer = LayerScene.Matches(LayerXaml)
            .Select(match => match.Groups["id"].Value)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();

        Assert.Equal(RegistryIds.OrderBy(id => id).ToArray(), inLayer);
    }

    [Fact]
    public void Every_scene_has_a_preview_card_and_the_gallery_has_no_unknown_scenes()
    {
        var inGallery = GalleryPreview.Matches(GalleryXaml)
            .Select(match => match.Groups["id"].Value)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();

        Assert.Equal(RegistryIds.OrderBy(id => id).ToArray(), inGallery);
    }

    [Fact]
    public void Preview_cards_are_visually_distinct()
    {
        var previews = GalleryPreview.Matches(GalleryXaml)
            .Select(match => (Id: match.Groups["id"].Value, Brush: Regex.Replace(match.Groups["brush"].Value, @"\s+", "")))
            .ToList();
        var duplicates = previews
            .GroupBy(preview => preview.Brush)
            .Where(group => group.Count() > 1)
            .Select(group => string.Join(" = ", group.Select(preview => preview.Id)))
            .ToList();

        Assert.True(duplicates.Count == 0,
            "Scenes with identical preview gradients: " + string.Join("; ", duplicates));
    }

    [Fact]
    public void The_HasLite_flag_matches_the_layer_structure()
    {
        var xaml = LayerXaml;
        var matches = LayerScene.Matches(xaml).Cast<Match>().ToList();
        Assert.NotEmpty(matches);

        var mismatches = new List<string>();
        for (var i = 0; i < matches.Count; i++)
        {
            var id = matches[i].Groups["id"].Value;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : xaml.Length;
            var segment = xaml[matches[i].Index..end];
            var hasFullLayers = segment.Contains("ShowFullScenes", StringComparison.Ordinal);
            var flagged = WallpaperScenes.Find(id)?.HasLite == true;
            if (hasFullLayers != flagged)
                mismatches.Add($"{id}: HasLite={flagged} but ShowFullScenes layers={hasFullLayers}");
        }

        Assert.True(mismatches.Count == 0, string.Join("; ", mismatches));
    }

    [Fact]
    public void Scene_panels_use_only_dynamic_accent_resources_for_color()
    {
        var xaml = LayerXaml;
        var start = xaml.IndexOf("ShowScene", StringComparison.Ordinal);
        Assert.True(start >= 0, "ShowScene panel not found in WallpaperLayer.axaml");

        var sceneArea = xaml[start..];
        var literals = Regex.Matches(sceneArea, @"Color=""#(?<hex>[0-9A-Fa-f]{6,8})""")
            .Select(match => match.Groups["hex"].Value.ToUpperInvariant())
            .Where(hex => hex != "00000000")
            .Distinct()
            .ToList();

        Assert.True(literals.Count == 0, "Hard-coded scene colors: " + string.Join(", ", literals));
    }
}