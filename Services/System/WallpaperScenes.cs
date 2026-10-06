using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace NullWave.Services;

public sealed record SceneDef : INotifyPropertyChanged
{
    public SceneDef(string id, string nameKey, string descriptionKey, bool hasLite, bool oledSafe)
    {
        Id = id;
        NameKey = nameKey;
        DescriptionKey = descriptionKey;
        HasLite = hasLite;
        OledSafe = oledSafe;
        LocalizationService.Instance.PropertyChanged += OnLanguageChanged;
    }

    public string Id { get; init; }
    public string NameKey { get; init; }
    public string DescriptionKey { get; init; }
    public bool HasLite { get; init; }
    public bool OledSafe { get; init; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name => LocalizationService.Instance[NameKey];
    public string Description => LocalizationService.Instance[DescriptionKey];

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is not (null or "Item[]")) return;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Description)));
    }
}

public static class WallpaperScenes
{
    public const string DefaultId = "aurora";

    public static readonly IReadOnlyList<SceneDef> All = new[]
    {
        new SceneDef("aurora", "Scene_aurora", "Scene_aurora_Desc", hasLite: true, oledSafe: false),
        new SceneDef("horizon", "Scene_horizon", "Scene_horizon_Desc", hasLite: true, oledSafe: false),
        new SceneDef("spotlight", "Scene_spotlight", "Scene_spotlight_Desc", hasLite: false, oledSafe: true),
        new SceneDef("duotone", "Scene_duotone", "Scene_duotone_Desc", hasLite: false, oledSafe: false),
        new SceneDef("dusk", "Scene_dusk", "Scene_dusk_Desc", hasLite: true, oledSafe: true),
    };

    public static SceneDef? Find(string? id) =>
        string.IsNullOrWhiteSpace(id)
            ? null
            : All.FirstOrDefault(scene => string.Equals(scene.Id, id, StringComparison.OrdinalIgnoreCase));
}