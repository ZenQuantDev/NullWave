using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace NullWave.Services;

public sealed record BuiltInDef : INotifyPropertyChanged
{
    public BuiltInDef(string id, string nameKey, string descriptionKey, string assetPath, bool oledSafe, bool exclusive)
    {
        Id = id;
        NameKey = nameKey;
        DescriptionKey = descriptionKey;
        AssetPath = assetPath;
        OledSafe = oledSafe;
        Exclusive = exclusive;
        LocalizationService.Instance.PropertyChanged += OnLanguageChanged;
    }

    public string Id { get; init; }
    public string NameKey { get; init; }
    public string DescriptionKey { get; init; }
    public string AssetPath { get; init; }
    public bool OledSafe { get; init; }
    public bool Exclusive { get; init; }
    public string Name => LocalizationService.Instance[NameKey];
    public string Description => LocalizationService.Instance[DescriptionKey];

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is not (null or "Item[]")) return;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Description)));
    }
}

public static class WallpaperBuiltIns
{
    public const string StarryNightId = "starrynight";

    public static readonly BuiltInDef StarryNight = new(
        StarryNightId,
        "BuiltIn_starrynight",
        "BuiltIn_starrynight_Desc",
        "avares://NullWave/Assets/Art/starry-night.jpg",
        oledSafe: false,
        exclusive: true);

    public static readonly IReadOnlyList<BuiltInDef> All = new[] { StarryNight };

    public static BuiltInDef? Find(string? id) =>
        string.IsNullOrWhiteSpace(id)
            ? null
            : All.FirstOrDefault(definition => string.Equals(definition.Id, id, StringComparison.OrdinalIgnoreCase));

    public static bool IsUnlocked(BuiltInDef definition, IEnumerable<string> unlocked) =>
        !definition.Exclusive || unlocked.Contains(definition.Id, StringComparer.OrdinalIgnoreCase);

    /// <summary>Gallery-visible entries: locked exclusives are completely hidden, not teased.</summary>
    public static IReadOnlyList<BuiltInDef> Visible(IEnumerable<string> unlocked) =>
        All.Where(definition => IsUnlocked(definition, unlocked)).ToList();

    public static TapReward RewardFor(int taps) =>
        new(Lore: taps > 0 && taps % 7 == 0, SignatureAccent: taps == 14, ExclusiveUnlock: taps == 21);
}

public record TapReward(bool Lore, bool SignatureAccent, bool ExclusiveUnlock);