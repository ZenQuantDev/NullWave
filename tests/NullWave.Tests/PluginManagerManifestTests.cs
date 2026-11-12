using System;
using System.Threading;
using System.Threading.Tasks;
using NullWave.Plugins.Contracts;
using NullWave.Services.Plugins;
using Xunit;

namespace NullWave.Tests;

public class PluginManagerManifestTests
{
    private sealed class StubPlugin : IPlugin
    {
        public string Id => "stub";
        public string Name => "Stub";
        public string Description => "stub";
        public PluginState State { get; set; } = PluginState.Unavailable;
        public bool IsEnabled { get; set; } = true;
        public Task<bool> InitializeAsync(IPluginHost host, CancellationToken ct = default) => Task.FromResult(true);
        public Task ShutdownAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private static PluginManifest Manifest(string id) => new(
        id, "Stub", "Author", "1.2.3", "desc",
        new[] { ProviderKind.Radio }, false, PluginAvailability.Installed);

    [Fact]
    public void Register_keeps_the_discovery_manifest()
    {
        var manager = new PluginManager();
        var plugin = new StubPlugin();

        manager.Register(plugin, Manifest("stub"));

        Assert.Equal("1.2.3", manager.GetManifest(plugin)!.Version);
        Assert.False(manager.GetManifest(plugin)!.BuiltIn);
    }

    [Fact]
    public void Register_without_manifest_returns_null()
    {
        var manager = new PluginManager();
        var plugin = new StubPlugin();

        manager.Register(plugin);

        Assert.Null(manager.GetManifest(plugin));
    }

    [Fact]
    public void PluginRegistered_carries_the_manifest()
    {
        var manager = new PluginManager();
        PluginManifest? seen = null;
        manager.PluginRegistered += (_, manifest) => seen = manifest;

        manager.Register(new StubPlugin(), Manifest("stub"));

        Assert.Equal("stub", seen!.Id);
    }

    [Fact]
    public void Duplicate_name_does_not_fire_the_event_twice()
    {
        var manager = new PluginManager();
        var count = 0;
        manager.PluginRegistered += (_, _) => count++;

        manager.Register(new StubPlugin(), Manifest("stub"));
        manager.Register(new StubPlugin(), Manifest("stub"));

        Assert.Equal(1, count);
    }
}
