using System.Text.Json;
using NullWave.Plugins.Contracts;
using NullWave.Services.Plugins;
using Xunit;

namespace NullWave.Tests;

public class PluginManifestTests
{
    [Fact]
    public void Deserialize_WithAllFields_Succeeds()
    {
        var json = @"{
            ""id"": ""com.nullwave.test"",
            ""name"": ""Test Plugin"",
            ""version"": ""1.2.3"",
            ""author"": ""Test Author"",
            ""description"": ""A test plugin"",
            ""kinds"": [""Download"", ""Metadata""],
            ""builtIn"": false,
            ""availability"": ""Installable"",
            ""entryPoint"": ""Test.Plugin, Test.Assembly"",
            ""capabilities"": {
                ""canDownload"": true,
                ""canFetchMetadata"": true
            },
            ""permissions"": {
                ""network"": true,
                ""accessApiKeys"": true,
                ""allowedDomains"": [""example.com""]
            },
            ""minHostVersion"": ""0.7.0"",
            ""license"": ""MIT""
        }";

        var manifest = JsonSerializer.Deserialize<PluginManifest>(json, PluginDiscovery.ManifestJsonOptions);

        Assert.NotNull(manifest);
        Assert.Equal("com.nullwave.test", manifest!.Id);
        Assert.Equal("Test Plugin", manifest.Name);
        Assert.Equal("1.2.3", manifest.Version);
        Assert.Equal("Test Author", manifest.Author);
        Assert.Equal("A test plugin", manifest.Description);
        Assert.Equal(2, manifest.Kinds.Length);
        Assert.Contains(ProviderKind.Download, manifest.Kinds);
        Assert.Contains(ProviderKind.Metadata, manifest.Kinds);
        Assert.False(manifest.BuiltIn);
        Assert.Equal(PluginAvailability.Installable, manifest.Availability);
        Assert.Equal("Test.Plugin, Test.Assembly", manifest.EntryPoint);
        Assert.True(manifest.Capabilities.CanDownload);
        Assert.True(manifest.Capabilities.CanFetchMetadata);
        Assert.True(manifest.Permissions.Network);
        Assert.True(manifest.Permissions.AccessApiKeys);
        Assert.Single(manifest.Permissions.AllowedDomains);
        Assert.Equal("example.com", manifest.Permissions.AllowedDomains[0]);
        Assert.Equal("0.7.0", manifest.MinHostVersion);
        Assert.Equal("MIT", manifest.License);
    }

    [Fact]
    public void Deserialize_WithMinimalFields_Succeeds()
    {
        var json = @"{
            ""id"": ""com.nullwave.test"",
            ""name"": ""Test Plugin"",
            ""version"": ""1.0.0"",
            ""author"": ""Test Author"",
            ""description"": ""A test plugin"",
            ""kinds"": [""Download""],
            ""builtIn"": false,
            ""availability"": ""Installed""
        }";

        var manifest = JsonSerializer.Deserialize<PluginManifest>(json, PluginDiscovery.ManifestJsonOptions);

        Assert.NotNull(manifest);
        Assert.Equal("com.nullwave.test", manifest!.Id);
        Assert.Equal(string.Empty, manifest.EntryPoint);
        Assert.NotNull(manifest.Capabilities);
        Assert.NotNull(manifest.Permissions);
        Assert.Null(manifest.MinHostVersion);
        Assert.Null(manifest.License);
    }

    [Fact]
    public void Deserialize_CaseInsensitiveProperties()
    {
        var json = @"{
            ""ID"": ""com.nullwave.test"",
            ""Name"": ""Test Plugin"",
            ""VERSION"": ""1.0.0"",
            ""Author"": ""Test Author"",
            ""Description"": ""A test plugin"",
            ""Kinds"": [""Download""],
            ""BuiltIn"": false,
            ""Availability"": ""Installed""
        }";

        var manifest = JsonSerializer.Deserialize<PluginManifest>(json, PluginDiscovery.ManifestJsonOptions);

        Assert.NotNull(manifest);
        Assert.Equal("com.nullwave.test", manifest!.Id);
        Assert.Equal("Test Plugin", manifest.Name);
        Assert.Equal("1.0.0", manifest.Version);
    }

    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        var original = new PluginManifest(
            Id: "com.nullwave.test",
            Name: "Test Plugin",
            Version: "1.2.3",
            Author: "Test Author",
            Description: "A test plugin",
            Kinds: new[] { ProviderKind.Download, ProviderKind.Metadata },
            BuiltIn: false,
            Availability: PluginAvailability.Planned)
        {
            EntryPoint = "Test.Plugin, Test.Assembly",
            Capabilities = new PluginCapabilities { CanDownload = true },
            Permissions = new PluginPermissions { Network = true },
            MinHostVersion = "0.7.0",
            License = "MIT"
        };

        var json = JsonSerializer.Serialize(original, PluginDiscovery.ManifestJsonOptions);
        var deserialized = JsonSerializer.Deserialize<PluginManifest>(json, PluginDiscovery.ManifestJsonOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(original.Id, deserialized!.Id);
        Assert.Equal(original.Name, deserialized.Name);
        Assert.Equal(original.Version, deserialized.Version);
        Assert.Equal(original.Author, deserialized.Author);
        Assert.Equal(original.Description, deserialized.Description);
        Assert.Equal(original.Kinds, deserialized.Kinds);
        Assert.Equal(original.BuiltIn, deserialized.BuiltIn);
        Assert.Equal(original.Availability, deserialized.Availability);
        Assert.Equal(original.EntryPoint, deserialized.EntryPoint);
        Assert.Equal(original.Capabilities.CanDownload, deserialized.Capabilities.CanDownload);
        Assert.Equal(original.Permissions.Network, deserialized.Permissions.Network);
        Assert.Equal(original.MinHostVersion, deserialized.MinHostVersion);
        Assert.Equal(original.License, deserialized.License);
    }
}