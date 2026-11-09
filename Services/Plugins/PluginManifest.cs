using System;
using System.Collections.Generic;

namespace NullWave.Services.Plugins;

public enum PluginAvailability { Installed, Installable, Planned }

public sealed record PluginManifest(
    string Id,
    string Name,
    string Author,
    string Version,
    string Description,
    ProviderKind[] Kinds,
    bool BuiltIn,
    PluginAvailability Availability)
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

public interface IPluginCatalog
{
    IReadOnlyList<PluginManifest> StoreEntries { get; }
}

/// <summary>
/// Phase 1: static catalog of planned plugins. All entries are "Planned".
/// Phase 2 will replace this with a signed JSON feed fetched at runtime.
/// </summary>
public sealed class StaticPluginCatalog : IPluginCatalog
{
    public IReadOnlyList<PluginManifest> StoreEntries { get; } = new[]
    {
        new PluginManifest(
            "radio-browser",
            "Radio Browser",
            "NullWave",
            "—",
            "Browse and import community radio stations from the Radio Browser directory",
            new[] { ProviderKind.Radio },
            false,
            PluginAvailability.Planned),

        new PluginManifest(
            "librivox",
            "LibriVox",
            "NullWave",
            "—",
            "Free public-domain audiobooks from the LibriVox archive",
            new[] { ProviderKind.Radio },
            false,
            PluginAvailability.Planned),

        new PluginManifest(
            "spotify",
            "Spotify",
            "NullWave",
            "—",
            "Import playlists and fetch metadata from Spotify (Premium account required)",
            new[] { ProviderKind.Metadata, ProviderKind.PlaylistImport },
            false,
            PluginAvailability.Planned),

        new PluginManifest(
            "soundcloud",
            "SoundCloud",
            "NullWave",
            "—",
            "Search and import tracks from SoundCloud",
            new[] { ProviderKind.Search, ProviderKind.Download },
            false,
            PluginAvailability.Planned),

        new PluginManifest(
            "bandcamp",
            "Bandcamp",
            "NullWave",
            "—",
            "Browse artists and import albums from Bandcamp",
            new[] { ProviderKind.Search, ProviderKind.Download },
            false,
            PluginAvailability.Planned),
    };
}