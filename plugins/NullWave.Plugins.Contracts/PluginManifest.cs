using System.Text.Json.Serialization;

namespace NullWave.Plugins.Contracts;

public sealed record PluginManifest
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("version")]
    public required string Version { get; init; }

    [JsonPropertyName("author")]
    public required string Author { get; init; }

    [JsonPropertyName("description")]
    public required string Description { get; init; }

    [JsonPropertyName("kinds")]
    public required ProviderKind[] Kinds { get; init; }

    [JsonPropertyName("entryPoint")]
    public required string EntryPoint { get; init; }

    [JsonPropertyName("settingsViewType")]
    public string? SettingsViewType { get; init; }
}
