using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NullWave.Plugins.Contracts;
using NullWave.Services;
using NullWave.Services.Plugins;
using NullWave.Services.Security;
using Xunit;

namespace NullWave.Tests;

public class ExternalRadioProviderAdapterTests
{
    private sealed class FakeRadio : IRadioProvider
    {
        public string Id => "fake";
        public string Name => "Fake Radio";
        public string Description => "fake";
        public PluginState State { get; set; } = PluginState.Available;
        public bool IsEnabled { get; set; } = true;
        public IPluginHost? LastHost;
        public string? LastQuery;
        public string? LastStationId;

        public Task<bool> InitializeAsync(IPluginHost host, CancellationToken ct = default)
        {
            LastHost = host;
            return Task.FromResult(true);
        }

        public Task ShutdownAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<List<RadioStationInfo>> SearchStationsAsync(
            string query, int limit = 50, CancellationToken ct = default)
        {
            LastQuery = query;
            return Task.FromResult(new List<RadioStationInfo>
                { new("s1", query, null, null, null, "http://s", null) });
        }

        public Task<List<RadioStationInfo>> GetTopStationsAsync(int limit = 50, CancellationToken ct = default) =>
            Task.FromResult(new List<RadioStationInfo>
                { new("t1", "Top", null, null, null, "http://t", null) });

        public Task<RadioStationInfo?> GetStationAsync(string stationId, CancellationToken ct = default)
        {
            LastStationId = stationId;
            return Task.FromResult<RadioStationInfo?>(
                new RadioStationInfo(stationId, "ByUuid", null, null, null, "http://u", null));
        }

        public Task<string?> ResolveStreamUrlAsync(string stationId, CancellationToken ct = default)
        {
            LastStationId = stationId;
            return Task.FromResult<string?>("http://r/" + stationId);
        }
    }

    private static PluginManifest Manifest() => new(
        "radiobrowser", "Radio Browser", "NullWave Team", "1.0.0", "desc",
        new[] { ProviderKind.Radio }, false, PluginAvailability.Installed);

    [Fact]
    public void Descriptor_comes_from_the_manifest_not_the_plugin()
    {
        var adapter = new ExternalRadioProviderAdapter(new FakeRadio(), Manifest());

        Assert.Equal("radiobrowser", adapter.Descriptor.Id);
        Assert.Equal(ProviderKind.Radio, adapter.Descriptor.Kind);
        Assert.Equal("Radio Browser", adapter.Descriptor.DisplayName);
        Assert.Equal("radiobrowser", adapter.Descriptor.PluginId);
    }

    [Fact]
    public void State_and_IsEnabled_delegate_both_ways()
    {
        var fake = new FakeRadio();
        var adapter = new ExternalRadioProviderAdapter(fake, Manifest());

        adapter.IsEnabled = false;
        fake.State = PluginState.Error;

        Assert.False(fake.IsEnabled);
        Assert.Equal(PluginState.Error, adapter.State);
    }

    [Fact]
    public async Task InitializeAsync_forwards_the_host()
    {
        var fake = new FakeRadio();
        var adapter = new ExternalRadioProviderAdapter(fake, Manifest());
        var host = new PluginHost(
            new PreferencesService(),
            new KeyStoreService(Path.Combine(Path.GetTempPath(), "nw-adapter-test.enc")),
            Manifest());

        Assert.True(await adapter.InitializeAsync(host));
        Assert.Same(host, fake.LastHost);
    }

    [Fact]
    public async Task Search_and_top_delegate_to_the_plugin()
    {
        var fake = new FakeRadio();
        var adapter = new ExternalRadioProviderAdapter(fake, Manifest());

        var found = await adapter.SearchStationsAsync("jazz");

        Assert.Equal("jazz", fake.LastQuery);
        Assert.Single(found);
        Assert.Single(await adapter.GetTopStationsAsync());
    }

    [Fact]
    public async Task GetStation_and_resolve_delegate_the_station_id()
    {
        var fake = new FakeRadio();
        var adapter = new ExternalRadioProviderAdapter(fake, Manifest());

        var station = await adapter.GetStationAsync("uuid-1");

        Assert.Equal("uuid-1", fake.LastStationId);
        Assert.Equal("ByUuid", station!.Name);
        Assert.Equal("http://r/uuid-2", await adapter.ResolveStreamUrlAsync("uuid-2"));
    }
}
