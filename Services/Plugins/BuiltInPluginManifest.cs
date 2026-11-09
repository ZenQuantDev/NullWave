using System;
using NullWave.Plugins.Contracts;

namespace NullWave.Services.Plugins;

/// <summary>
/// Factory for creating PluginManifest instances from built-in IPlugin/IProvider objects.
/// Lives in the main app (not Contracts) because it references IProvider.
/// </summary>
public static class BuiltInPluginManifest
{
    public static PluginManifest FromBuiltIn(IPlugin p) => new(
        p is IProvider ip ? ip.Descriptor.Id : p.Name,
        p.Name,
        "NullWave",
        "built-in",
        p.Description,
        p is IProvider q ? new[] { q.Descriptor.Kind } : Array.Empty<ProviderKind>(),
        true,
        PluginAvailability.Installed);
}