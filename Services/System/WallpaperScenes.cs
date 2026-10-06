using System;
using System.Collections.Generic;
using System.Linq;

namespace NullWave.Services;

public record SceneDef(string Id, string NameKey, string DescriptionKey, bool HasLite, bool OledSafe);

public static class WallpaperScenes
{
    public const string DefaultId = "aurora";

    public static readonly IReadOnlyList<SceneDef> All = new[]
    {
        new SceneDef("aurora", "Scene_aurora", "Scene_aurora_Desc", HasLite: true, OledSafe: false),
        new SceneDef("horizon", "Scene_horizon", "Scene_horizon_Desc", HasLite: true, OledSafe: false),
        new SceneDef("spotlight", "Scene_spotlight", "Scene_spotlight_Desc", HasLite: false, OledSafe: true),
        new SceneDef("duotone", "Scene_duotone", "Scene_duotone_Desc", HasLite: false, OledSafe: false),
        new SceneDef("dusk", "Scene_dusk", "Scene_dusk_Desc", HasLite: true, OledSafe: true),
    };

    public static SceneDef? Find(string? id) =>
        string.IsNullOrWhiteSpace(id)
            ? null
            : All.FirstOrDefault(scene => string.Equals(scene.Id, id, StringComparison.OrdinalIgnoreCase));
}