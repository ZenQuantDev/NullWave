using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;
using NullWave.Helpers;
using NullWave.Services;
using NullWave.Plugins.Contracts;
using IPluginCatalog = NullWave.Plugins.Contracts.IPluginCatalog;

namespace NullWave.ViewModels.Settings;

internal static class PluginUi
{
    public static string L(string key) => LocalizationService.Instance[key];

    public static IReadOnlyList<string> Tags(ProviderKind[] kinds) =>
        kinds.Select(k => L($"Settings_Integrations_Group_{k}")).ToList();

    public static MaterialIconKind IconFor(ProviderKind[] kinds) =>
        (kinds.Length > 0 ? kinds[0] : (ProviderKind?)null) switch
        {
            ProviderKind.Download => MaterialIconKind.Download,
            ProviderKind.Metadata => MaterialIconKind.TagMultiple,
            ProviderKind.Weather => MaterialIconKind.WeatherPartlyCloudy,
            ProviderKind.Mood => MaterialIconKind.Brain,
            ProviderKind.Search => MaterialIconKind.Magnify,
            ProviderKind.Radio => MaterialIconKind.Radio,
            ProviderKind.PlaylistImport => MaterialIconKind.PlaylistPlus,
            _ => MaterialIconKind.Puzzle
        };

    public static bool Matches(PluginManifest m, string name, string desc, string q, string category)
    {
        if (!string.Equals(category, "All", StringComparison.Ordinal) &&
            !m.Kinds.Any(k => string.Equals(k.ToString(), category, StringComparison.Ordinal)))
            return false;

        if (string.IsNullOrWhiteSpace(q)) return true;

        bool Has(string s) => s != null && s.Contains(q.Trim(), StringComparison.OrdinalIgnoreCase);
        return Has(name) || Has(desc) || Has(m.Author) || Tags(m.Kinds).Any(Has);
    }
}

public partial class CategoryChipViewModel : ObservableObject
{
    public string Key { get; }
    public string Label => Key == "All"
        ? PluginUi.L("Settings_Plugins_All")
        : PluginUi.L($"Settings_Integrations_Group_{Key}");

    [ObservableProperty] private bool _isSelected;

    public CategoryChipViewModel(string key, bool selected)
    {
        Key = key;
        _isSelected = selected;
    }
}

public sealed class StoreEntryViewModel
{
    public PluginManifest Manifest { get; }

    public StoreEntryViewModel(PluginManifest m) => Manifest = m;

    public string Name => Manifest.Name;
    public string Author => Manifest.Author;
    public string Description => Manifest.Description;
    public string VersionLabel => Manifest.Version;
    public IReadOnlyList<string> KindTags => PluginUi.Tags(Manifest.Kinds);
    public MaterialIconKind PrimaryIcon => PluginUi.IconFor(Manifest.Kinds);

    public bool CanInstall => Manifest.Availability == PluginAvailability.Installable;

    public string ActionLabel => Manifest.Availability switch
    {
        PluginAvailability.Installed => PluginUi.L("Settings_Plugins_InstalledLabel"),
        PluginAvailability.Installable => PluginUi.L("Settings_Plugins_Install"),
        _ => PluginUi.L("Settings_Plugins_ComingSoon")
    };

    public bool Matches(string q, string cat) =>
        PluginUi.Matches(Manifest, Name, Description, q, cat);
}

public partial class IntegrationsHubViewModel : ObservableObject
{
    private readonly IReadOnlyList<PluginRowViewModel> _installed;
    private readonly IReadOnlyList<StoreEntryViewModel> _store;

    public IReadOnlyList<CategoryChipViewModel> Categories { get; }
    public ObservableCollection<PluginRowViewModel> VisibleInstalled { get; } = new();
    public ObservableCollection<StoreEntryViewModel> VisibleStore { get; } = new();

    [ObservableProperty] private string _tab = "Installed";
    [ObservableProperty] private string _search = string.Empty;
    [ObservableProperty] private string _category = "All";

    public bool IsInstalledTab => Tab == "Installed";
    public bool IsStoreTab => Tab == "Store";
    public bool HasNoResults => IsInstalledTab
        ? VisibleInstalled.Count == 0
        : VisibleStore.Count == 0;

    public IntegrationsHubViewModel(
        IEnumerable<PluginRowViewModel> installed,
        IPluginCatalog catalog)
    {
        _installed = installed.ToList();

        var installedIds = _installed.Select(r => r.Manifest.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _store = catalog.StoreEntries
            .Where(m => !installedIds.Contains(m.Id))
            .Select(m => new StoreEntryViewModel(m))
            .ToList();

        var kinds = _installed.SelectMany(r => r.Manifest.Kinds)
            .Concat(_store.SelectMany(s => s.Manifest.Kinds))
            .Distinct()
            .OrderBy(k => k)
            .Select(k => k.ToString())
            .ToList();

        var categoryList = new List<CategoryChipViewModel> { new("All", true) };
        categoryList.AddRange(kinds.Select(k => new CategoryChipViewModel(k, false)));
        Categories = categoryList;

        Refilter();
    }

    partial void OnTabChanged(string value)
    {
        OnPropertyChanged(nameof(IsInstalledTab));
        OnPropertyChanged(nameof(IsStoreTab));
        OnPropertyChanged(nameof(HasNoResults));
        Refilter();
    }

    partial void OnSearchChanged(string value) => Refilter();

    partial void OnCategoryChanged(string value)
    {
        foreach (var c in Categories)
            c.IsSelected = string.Equals(c.Key, value, StringComparison.Ordinal);
        Refilter();
    }

    [RelayCommand]
    private void SetTab(string tab) => Tab = tab;

    [RelayCommand]
    private void SetCategory(string key) => Category = key;

    private void Refilter()
    {
        VisibleInstalled.Clear();
        foreach (var r in _installed.Where(r => r.Matches(Search, Category)))
            VisibleInstalled.Add(r);

        VisibleStore.Clear();
        foreach (var s in _store.Where(s => s.Matches(Search, Category)))
            VisibleStore.Add(s);

        OnPropertyChanged(nameof(HasNoResults));
    }
}