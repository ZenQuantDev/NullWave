using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NullWave.Plugins.Contracts;

namespace NullWave.Services.Plugins;

/// <summary>
/// Bridges an external plugin's IRadioProvider to the internal IProvider registry.
/// </summary>
internal sealed class ExternalRadioProviderAdapter : IProvider, IRadioProvider
{
    private readonly IRadioProvider _inner;

    public ExternalRadioProviderAdapter(IRadioProvider inner, PluginManifest manifest)
    {
        _inner = inner;
        Descriptor = new ProviderDescriptor(
            Id: manifest.Id,
            Kind: ProviderKind.Radio,
            DisplayName: manifest.Name,
            PluginId: manifest.Id
        );
    }

    // --- IProvider implementation ---
    public ProviderDescriptor Descriptor { get; }

    // --- IPlugin delegation ---
    public string Id => _inner.Id;
    public string Name => _inner.Name;
    public string Description => _inner.Description;
    public PluginState State { get => _inner.State; set => _inner.State = value; }
    public bool IsEnabled { get => _inner.IsEnabled; set => _inner.IsEnabled = value; }
    public Task<bool> InitializeAsync(IPluginHost host, CancellationToken ct = default) => _inner.InitializeAsync(host, ct);
    public Task ShutdownAsync(CancellationToken ct = default) => _inner.ShutdownAsync(ct);

    // --- IRadioProvider delegation ---
    public Task<List<RadioStationInfo>> SearchStationsAsync(string query, int limit = 50, CancellationToken ct = default)
        => _inner.SearchStationsAsync(query, limit, ct);

    public Task<List<RadioStationInfo>> GetTopStationsAsync(int limit = 50, CancellationToken ct = default)
        => _inner.GetTopStationsAsync(limit, ct);

    public Task<string?> ResolveStreamUrlAsync(string stationId, CancellationToken ct = default)
        => _inner.ResolveStreamUrlAsync(stationId, ct);
}