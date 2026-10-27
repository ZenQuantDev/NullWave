namespace NullWave.Services.Plugins;

/// <summary>Identity and activation metadata for a provider.</summary>
public sealed record ProviderDescriptor(
    string Id,
    ProviderKind Kind,
    string DisplayName,
    string? PluginId = null,
    /// <summary>If set, activating this provider auto-locks the paired provider kind to the given ID.</summary>
    string? PairedWithProviderId = null
);