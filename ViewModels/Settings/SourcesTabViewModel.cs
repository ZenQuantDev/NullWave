using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NullWave.Services.Plugins;
using NullWave.Services;
using NullWave.Helpers;
using NullWave.Models;
using Serilog;
using NullWave.Plugins.Contracts;

namespace NullWave.ViewModels.Settings;

public partial class SourceRowViewModel : ObservableObject
{
    public IProvider Provider { get; }
    public ProviderDescriptor Descriptor => Provider.Descriptor;

    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private string _statusLabel = string.Empty;

    public string Id => Descriptor.Id;
    public string DisplayName => Descriptor.Id switch
    {
        "yt-dlp" => Descriptor.DisplayName.Replace(
            "yt-dlp",
            LocalizationService.Instance["Settings_Integrations_Provider_YtDlp"],
            StringComparison.Ordinal),
        "lastfm" => LocalizationService.Instance["Settings_Integrations_Provider_LastFm"],
        "openweather" => LocalizationService.Instance["Settings_Integrations_Provider_OpenWeather"],
        "ollama" => LocalizationService.Instance["Settings_Integrations_Provider_Ollama"],
        _ => Descriptor.DisplayName
    };
    public ProviderKind Kind => Descriptor.Kind;

    public SourceRowViewModel(IProvider provider)
    {
        Provider = provider;
        _isEnabled = provider.IsEnabled;
        _isActive = false;
        UpdateStatusLabel();
    }

    public void UpdateStatusLabel()
    {
        StatusLabel = Provider.State switch
        {
            PluginState.Available => $"✓ {LocalizationService.Instance["Settings_Integrations_Status_Available"]}",
            PluginState.Loading => $"⟳ {LocalizationService.Instance["Settings_Integrations_Status_Loading"]}",
            PluginState.Error => $"✗ {LocalizationService.Instance["Settings_Integrations_Status_Error"]}",
            PluginState.Unavailable => $"○ {LocalizationService.Instance["Settings_Integrations_Status_Unavailable"]}",
            PluginState.Disabled => $"⊘ {LocalizationService.Instance["Settings_Integrations_Status_Disabled"]}",
            _ => LocalizationService.Instance["Settings_Integrations_Status_Unknown"]
        };
        OnPropertyChanged(nameof(StatusLabel));
    }

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(DisplayName));
        UpdateStatusLabel();
    }
}

public partial class SourceKindGroupViewModel : ObservableObject
{
    public ProviderKind Kind { get; }
    private readonly string _nameKey;
    private readonly string _descriptionKey;
    public string DisplayName => LocalizationService.Instance[_nameKey];
    public string Description => LocalizationService.Instance[_descriptionKey];
    public bool IsSingleActive { get; }

    public ObservableCollection<SourceRowViewModel> Providers { get; } = new();

    [ObservableProperty] private SourceRowViewModel? _selectedProvider;
    [ObservableProperty] private string _pairingMessage = string.Empty;
    private string? _pairedProviderId;

    public SourceKindGroupViewModel(ProviderKind kind, string nameKey, string descriptionKey, bool isSingleActive)
    {
        Kind = kind;
        _nameKey = nameKey;
        _descriptionKey = descriptionKey;
        IsSingleActive = isSingleActive;
    }

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(Description));
        PairingMessage = _pairedProviderId is null
            ? string.Empty
            : string.Format(LocalizationService.Instance["Settings_Integrations_PairedWith"], _pairedProviderId);
        foreach (var provider in Providers)
            provider.RefreshLocalization();
    }

    public void SetPairedProvider(string? providerId)
    {
        _pairedProviderId = providerId;
        RefreshLocalization();
    }
    
    public Action<SourceRowViewModel>? SelectionChanged { get; set; }

    partial void OnSelectedProviderChanged(SourceRowViewModel? value)
    {
        if (value != null && SelectionChanged != null)
        {
            SelectionChanged.Invoke(value);
        }
    }
}

public partial class SourcesTabViewModel : ObservableObject, IDisposable
{
    private readonly ProviderRegistry _registry;
    private readonly PreferencesService _prefs;
    private bool _building;

    public ObservableCollection<SourceKindGroupViewModel> KindGroups { get; } = new();

    // --- Consent Gate Properties ---
    public string ConsentStatus => _prefs.Current.DownloaderConsent is { IsCurrent: true } c
        ? string.Format(LocalizationService.Instance["Settings_Downloads_Consent_Accepted"], c.AcceptedAtUtc.ToLocalTime().ToString("d"), c.AcceptedVersion)
        : LocalizationService.Instance["Settings_Downloads_Consent_NotAccepted"];

    public bool IsConsentAccepted => _prefs.Current.DownloaderConsent is { IsCurrent: true };

    [RelayCommand]
    private void RevokeConsent()
    {
        _prefs.Update(p => p.DownloaderConsent = null);
        OnPropertyChanged(nameof(ConsentStatus));
        OnPropertyChanged(nameof(IsConsentAccepted));
        ToastService.Instance.Show(LocalizationService.Instance["Settings_Downloads_Consent_Revoked"], type: ToastType.Info, scope: "download");
    }

    public SourcesTabViewModel(ProviderRegistry registry, PreferencesService prefs)
    {
        _registry = registry;
        _prefs = prefs;
        
        // FIX: Refresh UI immediately when consent is accepted/revoked elsewhere (e.g. via Dialog)
        _prefs.Changed += OnPrefsChanged;
        LocalizationService.Instance.PropertyChanged += OnLocalizationChanged;
        
        BuildGroups();
        _registry.ProvidersChanged += OnProvidersChanged;
    }

    private void OnPrefsChanged(HashSet<string> changedKeys)
    {
        OnPropertyChanged(nameof(ConsentStatus));
        OnPropertyChanged(nameof(IsConsentAccepted));
    }

    private void OnLocalizationChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(ConsentStatus));
        foreach (var group in KindGroups)
            group.RefreshLocalization();
    }

    public void Dispose()
    {
        _prefs.Changed -= OnPrefsChanged;
        _registry.ProvidersChanged -= OnProvidersChanged;
        LocalizationService.Instance.PropertyChanged -= OnLocalizationChanged;
    }

    private void OnProvidersChanged()
    {
        Dispatcher.UIThread.Post(BuildGroups);
    }

    private void BuildGroups()
    {
        _building = true;
        try 
        { 
            KindGroups.Clear();

            var kinds = new (ProviderKind Kind, string NameKey, string DescKey, bool Single)[]
            {
                (ProviderKind.Download, "Settings_Integrations_Group_Download", "Settings_Integrations_Group_Download_Desc", true),
                (ProviderKind.Metadata, "Settings_Integrations_Group_Metadata", "Settings_Integrations_Group_Metadata_Desc", true),
                (ProviderKind.Search, "Settings_Integrations_Group_Search", "Settings_Integrations_Group_Search_Desc", false),
                (ProviderKind.Mood, "Settings_Integrations_Group_Mood", "Settings_Integrations_Group_Mood_Desc", true),
                (ProviderKind.Weather, "Settings_Integrations_Group_Weather", "Settings_Integrations_Group_Weather_Desc", true),
                (ProviderKind.Radio, "Settings_Integrations_Group_Radio", "Settings_Integrations_Group_Radio_Desc", false),
                (ProviderKind.PlaylistImport, "Settings_Integrations_Group_PlaylistImport", "Settings_Integrations_Group_PlaylistImport_Desc", false),
            };

            foreach (var (kind, nameKey, descKey, single) in kinds)
            {
                var providers = _registry.List(kind);
                if (providers.Count == 0) continue;

                var group = new SourceKindGroupViewModel(kind, nameKey, descKey, single);

                foreach (var provider in providers)
                {
                    var row = new SourceRowViewModel(provider);
                    var active = _registry.GetActive(kind);
                    row.IsActive = active?.Descriptor.Id == provider.Descriptor.Id;
                    group.Providers.Add(row);
                }

                if (single && group.Providers.Count > 0)
                {
                    group.SelectedProvider = group.Providers.FirstOrDefault(p => p.IsActive) ?? group.Providers[0];
                }

                var activeProvider = _registry.GetActive(kind);
                if (activeProvider?.Descriptor.PairedWithProviderId != null)
                {
                    group.SetPairedProvider(activeProvider.Descriptor.PairedWithProviderId);
                }

                group.SelectionChanged = row => 
                { 
                    if (!_building) 
                    {
                        _registry.SetActive(row.Kind, row.Id);
                        Log.Information("[Sources] Activated provider {Id} for kind {Kind}", row.Id, row.Kind);
                    }
                };

                KindGroups.Add(group);
            }
        }
        finally 
        { 
            _building = false; 
        }
        OnPropertyChanged(nameof(RoutableGroups));
        OnPropertyChanged(nameof(HasRoutableGroups));
    }

    public IEnumerable<SourceKindGroupViewModel> RoutableGroups =>
    KindGroups.Where(g => g.Providers.Count > 1).ToList();

    public bool HasRoutableGroups =>
        KindGroups.Any(g => g.Providers.Count > 1);

    [RelayCommand]
    private void SelectProvider(SourceRowViewModel? row)
    {
        if (row == null) return;
        _registry.SetActive(row.Kind, row.Id);
        Log.Information("[Sources] Activated provider {Id} for kind {Kind}", row.Id, row.Kind);
        BuildGroups();
    }

    [RelayCommand]
    private void ToggleProvider(SourceRowViewModel? row)
    {
        if (row == null) return;
        Log.Information("[Sources] Toggled provider {Id} for kind {Kind}", row.Id, row.Kind);
        BuildGroups();
    }
}