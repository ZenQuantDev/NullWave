using System.Threading;
using System.Threading.Tasks;
using NullWave.Plugins.Contracts;

namespace NullWave.Services.Plugins;

/// <summary>
/// Bridges an external plugin's IRadioProvider to the internal IProvider registry.
/// </summary>
internal sealed class ExternalRadioProviderAdapter : IProvider
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

    /// <summary>Exposes the inner plugin so the UI can call SearchStationsAsync.</summary>
    public IRadioProvider Inner => _inner;

    // --- IPlugin delegation ---
    public string Id => _inner.Id;
    public string Name => _inner.Name;
    public string Description => _inner.Description;
    public PluginState State { get => _inner.State; set => _inner.State = value; }
    public bool IsEnabled { get => _inner.IsEnabled; set => _inner.IsEnabled = value; }
    public Task<bool> InitializeAsync(IPluginHost host, CancellationToken ct = default) => _inner.InitializeAsync(host, ct);
    public Task ShutdownAsync(CancellationToken ct = default) => _inner.ShutdownAsync(ct);

    // --- IProvider implementation ---
    public ProviderDescriptor Descriptor { get; }
}