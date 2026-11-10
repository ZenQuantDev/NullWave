using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace NullWave.Plugins.Contracts;

public enum PluginAvailability { Installed, Installable, Planned }

/// <summary>
/// Metadata describing a plugin's identity, capabilities, and requirements.
/// </summary>
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
    [JsonPropertyName("entryPoint")]
    public string EntryPoint { get; init; } = string.Empty;

    [JsonPropertyName("capabilities")]
    public PluginCapabilities Capabilities { get; init; } = new();

    [JsonPropertyName("permissions")]
    public PluginPermissions Permissions { get; init; } = new();

    [JsonPropertyName("minHostVersion")]
    public string? MinHostVersion { get; init; }

    [JsonPropertyName("license")]
    public string? License { get; init; }
}

public sealed record PluginCapabilities
{
    [JsonPropertyName("canDownload")]
    public bool CanDownload { get; init; }

    [JsonPropertyName("canFetchMetadata")]
    public bool CanFetchMetadata { get; init; }

    [JsonPropertyName("canSearch")]
    public bool CanSearch { get; init; }

    [JsonPropertyName("canImportPlaylists")]
    public bool CanImportPlaylists { get; init; }

    [JsonPropertyName("canProvideRadio")]
    public bool CanProvideRadio { get; init; }

    [JsonPropertyName("canRankTracks")]
    public bool CanRankTracks { get; init; }

    [JsonPropertyName("canProvideWeather")]
    public bool CanProvideWeather { get; init; }
}

public sealed record PluginPermissions
{
    [JsonPropertyName("network")]
    public bool Network { get; init; }

    [JsonPropertyName("readLibrary")]
    public bool ReadLibrary { get; init; }

    [JsonPropertyName("writeLibrary")]
    public bool WriteLibrary { get; init; }

    [JsonPropertyName("executeProcesses")]
    public bool ExecuteProcesses { get; init; }

    [JsonPropertyName("accessApiKeys")]
    public bool AccessApiKeys { get; init; }

    [JsonPropertyName("allowedDomains")]
    public IReadOnlyList<string> AllowedDomains { get; init; } = Array.Empty<string>();
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
            "radio-browser", "Radio Browser", "NullWave", "-",
            "Browse and import community radio stations from the Radio Browser directory",
            new[] { ProviderKind.Radio }, false, PluginAvailability.Planned),

        new PluginManifest(
            "librivox", "LibriVox", "NullWave", "-",
            "Free public-domain audiobooks from the LibriVox archive",
            new[] { ProviderKind.Radio }, false, PluginAvailability.Planned),

        new PluginManifest(
            "spotify", "Spotify", "NullWave", "-",
            "Import playlists and fetch metadata from Spotify (Premium account required)",
            new[] { ProviderKind.Metadata, ProviderKind.PlaylistImport }, false, PluginAvailability.Planned),

        new PluginManifest(
            "soundcloud", "SoundCloud", "NullWave", "-",
            "Search and import tracks from SoundCloud",
            new[] { ProviderKind.Search, ProviderKind.Download }, false, PluginAvailability.Planned),

        new PluginManifest(
            "bandcamp", "Bandcamp", "NullWave", "-",
            "Browse artists and import albums from Bandcamp",
            new[] { ProviderKind.Search, ProviderKind.Download }, false, PluginAvailability.Planned),
    };
}