using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Serilog;

namespace NullWave.Services.Plugins;

/// <summary>
/// Nuclear-style provider registry. Tracks which providers are registered
/// and which is active per kind. Thread-safe. Persists user choices via
/// the PreferencesService (injected).
/// </summary>
public sealed class ProviderRegistry
{
    private readonly ConcurrentDictionary<string, IProvider> _providers = new();
    private readonly ConcurrentDictionary<ProviderKind, string> _activeByKind = new();
    private readonly PreferencesService _prefs;
    private readonly ILogger _logger;

    public event Action? ProvidersChanged;

    public ProviderRegistry(PreferencesService prefs, ILogger? logger = null)
    {
        _prefs = prefs;
        _logger = logger ?? Log.ForContext<ProviderRegistry>();
        LoadPersistedChoices();
    }

    public string Register(IProvider provider)
    {
        var id = provider.Descriptor.Id;
        if (!_providers.TryAdd(id, provider))
        {
            _logger.Warning("Provider {Id} already registered, skipping", id);
            return id;
        }
        var kind = provider.Descriptor.Kind;
        // Check persisted choice NOW that provider is registered
        var persisted = GetPersistedChoice(kind);
        if (persisted == id || (persisted != null && _providers.ContainsKey(persisted)))
        {
            _activeByKind[kind] = persisted ?? id;
        }
        else if (!_activeByKind.ContainsKey(kind))
        {
            _activeByKind[kind] = id;
        }
        _logger.Information("Registered provider: {Id} (kind={Kind}, name={Name})",
            id, kind, provider.Descriptor.DisplayName);

        // Handle pairing locks: if this provider forces a paired provider, activate it.
        if (provider.Descriptor.PairedWithProviderId is { } pairedId
            && _providers.TryGetValue(pairedId, out _))
        {
            // Find the kind of the paired provider and activate it
            var paired = _providers[pairedId];
            _activeByKind[paired.Descriptor.Kind] = pairedId;
        }

        ProvidersChanged?.Invoke();
        return id;
    }

    public bool Unregister(string id)
    {
        if (!_providers.TryRemove(id, out var provider))
            return false;

        // If this was the active provider for its kind, clear the choice
        var kind = provider.Descriptor.Kind;
        if (_activeByKind.TryGetValue(kind, out var activeId) && activeId == id)
        {
            _activeByKind.TryRemove(kind, out _);
            // Fall back to another registered provider of the same kind if one exists
            var fallback = _providers.Values.FirstOrDefault(p => p.Descriptor.Kind == kind);
            if (fallback != null)
                _activeByKind[kind] = fallback.Descriptor.Id;
        }

        ProvidersChanged?.Invoke();
        return true;
    }

    public IReadOnlyList<IProvider> List(ProviderKind? kind = null)
    {
        var all = _providers.Values.AsEnumerable();
        if (kind.HasValue)
            all = all.Where(p => p.Descriptor.Kind == kind.Value);
        return all.ToList();
    }

    public IProvider? Get(string id) =>
        _providers.TryGetValue(id, out var p) ? p : null;

    public IProvider? GetActive(ProviderKind kind) =>
        _activeByKind.TryGetValue(kind, out var id) && _providers.TryGetValue(id, out var p)
            ? p
            : null;

    public IEnumerable<IProvider> GetAllActive(ProviderKind kind) =>
        kind.IsSingleActive()
            ? (GetActive(kind) is { } single ? new[] { single } : Array.Empty<IProvider>())
            : _providers.Values.Where(p => p.Descriptor.Kind == kind && p.IsEnabled && p.State == PluginState.Available);

    public void SetActive(ProviderKind kind, string id)
    {
        if (!_providers.TryGetValue(id, out var provider))
        {
            _logger.Warning("Cannot activate unknown provider {Id}", id);
            return;
        }
        if (provider.Descriptor.Kind != kind)
        {
            _logger.Warning("Provider {Id} is kind {Actual}, not {Requested}", id, provider.Descriptor.Kind, kind);
            return;
        }

        _activeByKind[kind] = id;
        PersistChoice(kind, id);
        ProvidersChanged?.Invoke();
    }

    // --- Persistence ---

    private string PrefsKey(ProviderKind kind) => $"ActiveProvider:{kind}";

    private string? GetPersistedChoice(ProviderKind kind)
    {
        // We store these as regular string preferences.
        // Using reflection-free access via the plugin configs bag is simpler.
        var cfg = _prefs.Current.PluginConfigs.FirstOrDefault(c => c.Name == "ProviderRegistry");
        return cfg?.Settings.TryGetValue(PrefsKey(kind), out var v) == true ? v : null;
    }

    private void PersistChoice(ProviderKind kind, string id)
    {
        _prefs.Update(p =>
        {
            var cfg = p.PluginConfigs.FirstOrDefault(c => c.Name == "ProviderRegistry");
            if (cfg == null)
            {
                cfg = new Models.PluginConfig { Name = "ProviderRegistry" };
                p.PluginConfigs.Add(cfg);
            }
            cfg.Settings[PrefsKey(kind)] = id;
        });
    }

    private void LoadPersistedChoices()
    {
        var cfg = _prefs.Current.PluginConfigs.FirstOrDefault(c => c.Name == "ProviderRegistry");
        if (cfg == null) return;
        foreach (var kind in Enum.GetValues<ProviderKind>())
        {
            if (cfg.Settings.TryGetValue(PrefsKey(kind), out var id))
                _activeByKind[kind] = id;
        }
    }
}