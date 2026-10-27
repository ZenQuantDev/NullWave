namespace NullWave.Services.Plugins;

/// <summary>
/// Marker interface for anything that participates in the ProviderRegistry.
/// Kind-specific interfaces (IDownloadProvider, IMetadataProvider, etc.) extend this.
/// </summary>
public interface IProvider : IPlugin
{
    ProviderDescriptor Descriptor { get; }
}