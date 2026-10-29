using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NullWave.Services.Plugins;
using Serilog;

namespace NullWave.ViewModels.Settings;

public partial class SourceRowViewModel : ObservableObject
{
    public IProvider Provider { get; }
    public ProviderDescriptor Descriptor => Provider.Descriptor;

    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private string _statusLabel = string.Empty;

    public string Id => Descriptor.Id;
    public string DisplayName => Descriptor.DisplayName;
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
            PluginState.Available => "✓ Available",
            PluginState.Loading => "⟳ Loading...",
            PluginState.Error => "✗ Error",
            PluginState.Unavailable => "○ Not installed",
            PluginState.Disabled => "⊘ Disabled",
            _ => "Unknown"
        };
        OnPropertyChanged(nameof(StatusLabel));
    }
}

public partial class SourceKindGroupViewModel : ObservableObject
{
    public ProviderKind Kind { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public bool IsSingleActive { get; }

    public ObservableCollection<SourceRowViewModel> Providers { get; } = new();

    [ObservableProperty] private SourceRowViewModel? _selectedProvider;
    [ObservableProperty] private string _pairingMessage = string.Empty;

    public SourceKindGroupViewModel(ProviderKind kind, string displayName, string description, bool isSingleActive)
    {
        Kind = kind;
        DisplayName = displayName;
        Description = description;
        IsSingleActive = isSingleActive;
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

public partial class SourcesTabViewModel : ObservableObject
{
    private readonly ProviderRegistry _registry;
    private bool _building;

    public ObservableCollection<SourceKindGroupViewModel> KindGroups { get; } = new();

    public SourcesTabViewModel(ProviderRegistry registry)
    {
        _registry = registry;
        BuildGroups();
        _registry.ProvidersChanged += OnProvidersChanged;
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

            var kinds = new (ProviderKind Kind, string Name, string Desc, bool Single)[]
            {
                (ProviderKind.Download, "Download", "Audio download engines", true),
                (ProviderKind.Metadata, "Metadata", "Track info, tags, and artwork", true),
                (ProviderKind.Search, "Search", "Multi-source track search", false),
                (ProviderKind.Mood, "Mood / AI", "AI ranking and mood analysis", true),
                (ProviderKind.Weather, "Weather", "Weather data for mood playlists", true),
                (ProviderKind.Radio, "Radio", "Radio station directories", false),
                (ProviderKind.PlaylistImport, "Playlist Import", "M3U and playlist file import", false),
            };

            foreach (var (kind, name, desc, single) in kinds)
            {
                var providers = _registry.List(kind);
                if (providers.Count == 0) continue;

                var group = new SourceKindGroupViewModel(kind, name, desc, single);

                foreach (var provider in providers)
                {
                    var row = new SourceRowViewModel(provider);
                    var active = _registry.GetActive(kind);
                    row.IsActive = active?.Descriptor.Id == provider.Descriptor.Id;
                    group.Providers.Add(row);
                }

                // For single-active kinds, set the selected provider
                if (single && group.Providers.Count > 0)
                {
                    group.SelectedProvider = group.Providers.FirstOrDefault(p => p.IsActive) ?? group.Providers[0];
                }

                // Check pairing locks
                var activeProvider = _registry.GetActive(kind);
                if (activeProvider?.Descriptor.PairedWithProviderId != null)
                {
                    group.PairingMessage = $"Locked to: {activeProvider.Descriptor.PairedWithProviderId}";
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
    }

    [RelayCommand]
    private void SelectProvider(SourceRowViewModel? row)
    {
        if (row == null) return;

        _registry.SetActive(row.Kind, row.Id);
        Log.Information("[Sources] Activated provider {Id} for kind {Kind}", row.Id, row.Kind);

        // Refresh all groups to update active states
        BuildGroups();
    }

    [RelayCommand]
    private void ToggleProvider(SourceRowViewModel? row)
    {
        if (row == null) return;

        // For multi-active kinds, toggling just enables/disables
        // The registry handles the rest
        Log.Information("[Sources] Toggled provider {Id} for kind {Kind}", row.Id, row.Kind);
        BuildGroups();
    }
}