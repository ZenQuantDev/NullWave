using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NullWave.Helpers;
using NullWave.Plugins.Contracts;
using NullWave.Services.Plugins;
using Xunit;

namespace NullWave.Tests;

public class PluginRootOrderTests : IDisposable
{
    private readonly string _root;

    public PluginRootOrderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nw-roots-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void User_root_is_probed_before_bundled_root()
    {
        Assert.Equal(NullWavePaths.PluginsDir, NullWavePaths.PluginRoots[0]);
        Assert.Equal(NullWavePaths.BundledPluginsDir, NullWavePaths.PluginRoots[1]);
    }

    [Fact]
    public void Directories_are_enumerated_in_root_order()
    {
        var user = Path.Combine(_root, "user");
        var bundled = Path.Combine(_root, "bundled");
        Directory.CreateDirectory(Path.Combine(user, "RadioBrowser"));
        Directory.CreateDirectory(Path.Combine(bundled, "RadioBrowser"));
        Directory.CreateDirectory(Path.Combine(bundled, "OpenWeather"));

        var directories = PluginDiscovery.EnumeratePluginDirectories(new[] { user, bundled }).ToArray();

        Assert.Equal(3, directories.Length);
        Assert.StartsWith(user, directories[0], StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(bundled, directories[1], StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(bundled, directories[2], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Missing_and_empty_roots_are_skipped()
    {
        var missing = Path.Combine(_root, "nope");
        var empty = Path.Combine(_root, "empty");
        Directory.CreateDirectory(empty);

        Assert.Empty(PluginDiscovery.EnumeratePluginDirectories(new[] { missing, empty, "" }));
    }

    [Fact]
    public async Task User_plugin_with_same_id_overrides_bundled_plugin()
    {
        var user = Path.Combine(_root, "user");
        var bundled = Path.Combine(_root, "bundled");
        var userPlugin = Path.Combine(user, "shared");
        var bundledPlugin = Path.Combine(bundled, "shared");
        Directory.CreateDirectory(userPlugin);
        Directory.CreateDirectory(bundledPlugin);

        var assemblyPath = typeof(PluginRootPrecedenceFixture).Assembly.Location;
        var assemblyName = Path.GetFileName(assemblyPath);
        File.Copy(assemblyPath, Path.Combine(userPlugin, assemblyName));
        File.Copy(assemblyPath, Path.Combine(bundledPlugin, assemblyName));
        await File.WriteAllTextAsync(
            Path.Combine(userPlugin, "plugin.json"),
            CreateManifest("User copy", assemblyName));
        await File.WriteAllTextAsync(
            Path.Combine(bundledPlugin, "plugin.json"),
            CreateManifest("Bundled copy", assemblyName));

        var discovery = new PluginDiscovery(new[] { user, bundled });
        var plugins = await discovery.DiscoverAndLoadAsync(
            _ => throw new InvalidOperationException("Disabled plugins must not request a host."),
            enabledState: _ => false);

        Assert.Single(plugins);
        Assert.Equal("User copy", plugins[0].Manifest.Name);
        discovery.UnloadAll();
    }

    private static string CreateManifest(string name, string assemblyFile)
    {
        var assemblyName = Path.GetFileNameWithoutExtension(assemblyFile);
        var manifest = new PluginManifest(
            "shared.plugin",
            name,
            "NullWave.Tests",
            "1.0.0",
            "Root precedence test fixture",
            new[] { ProviderKind.Radio },
            false,
            PluginAvailability.Installed)
        {
            EntryPoint = $"NullWave.Tests.PluginRootPrecedenceFixture, {assemblyName}"
        };
        return JsonSerializer.Serialize(manifest, PluginDiscovery.ManifestJsonOptions);
    }
}

public sealed class PluginRootPrecedenceFixture : IPlugin
{
    public string Id => "shared.plugin";
    public string Name => "Root precedence test fixture";
    public string Description => "Plugin discovery test fixture";
    public PluginState State { get; set; }
    public bool IsEnabled { get; set; }

    public Task<bool> InitializeAsync(IPluginHost host, CancellationToken ct = default)
        => Task.FromResult(true);

    public Task ShutdownAsync(CancellationToken ct = default) => Task.CompletedTask;
}
