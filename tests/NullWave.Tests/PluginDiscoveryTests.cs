using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using NullWave.Helpers;
using NullWave.Plugins.Contracts;
using NullWave.Services.Plugins;
using Xunit;

namespace NullWave.Tests;

public class PluginDiscoveryTests : IDisposable
{
    private readonly string _tempDir;

    public PluginDiscoveryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"nw-plugin-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void ManifestJsonOptions_DeserializesCaseInsensitive()
    {
        var json = @"{
            ""id"": ""test.plugin"",
            ""name"": ""Test Plugin"",
            ""version"": ""1.0.0"",
            ""author"": ""Test Author"",
            ""description"": ""Test description"",
            ""kinds"": [""Download""],
            ""entryPoint"": ""Test.Plugin, Test.Assembly""
        }";

        var manifest = JsonSerializer.Deserialize<PluginManifest>(json, PluginDiscovery.ManifestJsonOptions);

        Assert.NotNull(manifest);
        Assert.Equal("test.plugin", manifest!.Id);
        Assert.Equal("Test Plugin", manifest.Name);
        Assert.Equal("1.0.0", manifest.Version);
        Assert.Single(manifest.Kinds);
        Assert.Equal(ProviderKind.Download, manifest.Kinds[0]);
    }

    [Fact]
    public void ManifestJsonOptions_DeserializesStringEnums()
    {
        var json = @"{
            ""id"": ""test.plugin"",
            ""name"": ""Test Plugin"",
            ""version"": ""1.0.0"",
            ""author"": ""Test Author"",
            ""description"": ""Test description"",
            ""kinds"": [""Download"", ""Metadata"", ""Weather""],
            ""entryPoint"": ""Test.Plugin, Test.Assembly""
        }";

        var manifest = JsonSerializer.Deserialize<PluginManifest>(json, PluginDiscovery.ManifestJsonOptions);

        Assert.NotNull(manifest);
        Assert.Equal(3, manifest!.Kinds.Length);
        Assert.Contains(ProviderKind.Download, manifest.Kinds);
        Assert.Contains(ProviderKind.Metadata, manifest.Kinds);
        Assert.Contains(ProviderKind.Weather, manifest.Kinds);
    }

    [Fact]
    public async Task DiscoverAndLoadAsync_EmptyDirectory_ReturnsEmpty()
    {
        var discovery = new PluginDiscovery(_tempDir);
        var result = await discovery.DiscoverAndLoadAsync(_ => throw new InvalidOperationException("Should not be called"));

        Assert.Empty(result);
    }

    [Fact]
    public async Task DiscoverAndLoadAsync_InvalidManifest_SkipsPlugin()
    {
        var pluginDir = Path.Combine(_tempDir, "test-plugin");
        Directory.CreateDirectory(pluginDir);

        var invalidManifest = @"{
            ""id"": """",
            ""name"": ""Test""
        }";
        await File.WriteAllTextAsync(Path.Combine(pluginDir, "plugin.json"), invalidManifest);

        var discovery = new PluginDiscovery(_tempDir);
        var result = await discovery.DiscoverAndLoadAsync(_ => throw new InvalidOperationException("Should not be called"));

        Assert.Empty(result);
    }

    [Fact]
    public async Task DiscoverAndLoadAsync_MissingDll_SkipsPlugin()
    {
        var pluginDir = Path.Combine(_tempDir, "test-plugin");
        Directory.CreateDirectory(pluginDir);

        var manifest = @"{
            ""id"": ""test.plugin"",
            ""name"": ""Test Plugin"",
            ""version"": ""1.0.0"",
            ""author"": ""Test Author"",
            ""description"": ""Test description"",
            ""kinds"": [""Download""],
            ""entryPoint"": ""Test.Plugin, MissingAssembly""
        }";
        await File.WriteAllTextAsync(Path.Combine(pluginDir, "plugin.json"), manifest);

        var discovery = new PluginDiscovery(_tempDir);
        var result = await discovery.DiscoverAndLoadAsync(_ => throw new InvalidOperationException("Should not be called"));

        Assert.Empty(result);
    }

    [Fact]
    public async Task DiscoverAndLoadAsync_RejectsPluginRequiringNewerHost()
    {
        var pluginDir = Path.Combine(_tempDir, "newer-host-plugin");
        Directory.CreateDirectory(pluginDir);

        var manifest = $$"""
        {
            "id": "test.plugin",
            "name": "Test Plugin",
            "version": "1.0.0",
            "author": "Test Author",
            "description": "Test description",
            "kinds": ["Download"],
            "entryPoint": "Test.Plugin, MissingAssembly",
            "minHostVersion": "{{int.Parse(AppVersionInfo.Current.Split('.')[0]) + 1}}.0.0"
        }
        """;
        await File.WriteAllTextAsync(Path.Combine(pluginDir, "plugin.json"), manifest);

        var discovery = new PluginDiscovery(_tempDir);
        var result = await discovery.DiscoverAndLoadAsync(
            _ => throw new InvalidOperationException("Should not be called"));

        Assert.Empty(result);
    }

    [Fact]
    public async Task DiscoverAndLoadAsync_PathTraversal_Rejected()
    {
        var pluginDir = Path.Combine(_tempDir, "test-plugin");
        Directory.CreateDirectory(pluginDir);

        // Create a fake DLL outside the plugin directory
        var outsideDir = Path.Combine(_tempDir, "outside");
        Directory.CreateDirectory(outsideDir);
        var outsideDll = Path.Combine(outsideDir, "Evil.dll");
        await File.WriteAllTextAsync(outsideDll, "fake dll");

        var manifest = $@"{{
            ""id"": ""test.plugin"",
            ""name"": ""Test Plugin"",
            ""version"": ""1.0.0"",
            ""author"": ""Test Author"",
            ""description"": ""Test description"",
            ""kinds"": [""Download""],
            ""entryPoint"": ""Test.Plugin, ../outside/Evil""
        }}";
        await File.WriteAllTextAsync(Path.Combine(pluginDir, "plugin.json"), manifest);

        var discovery = new PluginDiscovery(_tempDir);
        var result = await discovery.DiscoverAndLoadAsync(_ => throw new InvalidOperationException("Should not be called"));

        Assert.Empty(result);
    }

    [Fact]
    public async Task DiscoverAndLoadAsync_MissingEntryPointField_SkipsPlugin()
    {
        var pluginDir = Path.Combine(_tempDir, "test-plugin");
        Directory.CreateDirectory(pluginDir);

        var manifest = @"{
            ""id"": ""test.plugin"",
            ""name"": ""Test Plugin"",
            ""version"": ""1.0.0"",
            ""author"": ""Test Author"",
            ""description"": ""Test description"",
            ""kinds"": [""Download""]
        }";
        await File.WriteAllTextAsync(Path.Combine(pluginDir, "plugin.json"), manifest);

        var discovery = new PluginDiscovery(_tempDir);
        var result = await discovery.DiscoverAndLoadAsync(_ => throw new InvalidOperationException("Should not be called"));

        Assert.Empty(result);
    }

    [Fact]
    public async Task DiscoverAndLoadAsync_InvalidEntryPointFormat_SkipsPlugin()
    {
        var pluginDir = Path.Combine(_tempDir, "test-plugin");
        Directory.CreateDirectory(pluginDir);

        var manifest = @"{
            ""id"": ""test.plugin"",
            ""name"": ""Test Plugin"",
            ""version"": ""1.0.0"",
            ""author"": ""Test Author"",
            ""description"": ""Test description"",
            ""kinds"": [""Download""],
            ""entryPoint"": ""InvalidFormatNoComma""
        }";
        await File.WriteAllTextAsync(Path.Combine(pluginDir, "plugin.json"), manifest);

        var discovery = new PluginDiscovery(_tempDir);
        var result = await discovery.DiscoverAndLoadAsync(_ => throw new InvalidOperationException("Should not be called"));

        Assert.Empty(result);
    }

    [Fact]
    public async Task DiscoverAndLoadAsync_MissingKinds_SkipsPlugin()
    {
        var pluginDir = Path.Combine(_tempDir, "test-plugin");
        Directory.CreateDirectory(pluginDir);

        var manifest = @"{
            ""id"": ""test.plugin"",
            ""name"": ""Test Plugin"",
            ""version"": ""1.0.0"",
            ""author"": ""Test Author"",
            ""description"": ""Test description"",
            ""kinds"": [],
            ""entryPoint"": ""Test.Plugin, Test.Assembly""
        }";
        await File.WriteAllTextAsync(Path.Combine(pluginDir, "plugin.json"), manifest);

        var discovery = new PluginDiscovery(_tempDir);
        var result = await discovery.DiscoverAndLoadAsync(_ => throw new InvalidOperationException("Should not be called"));

        Assert.Empty(result);
    }

    [Fact]
    public void UnloadAll_ClearsContexts()
    {
        var discovery = new PluginDiscovery(_tempDir);
        
        // UnloadAll should not throw even when no plugins are loaded
        discovery.UnloadAll();
    }
}