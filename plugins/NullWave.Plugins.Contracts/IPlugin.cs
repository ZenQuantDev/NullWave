using System.Threading;
using System.Threading.Tasks;

namespace NullWave.Plugins.Contracts;

public interface IPlugin
{
    string Id { get; }
    string Name { get; }
    string Description { get; }
    PluginState State { get; set; }
    bool IsEnabled { get; set; }

    Task InitializeAsync(IPluginHost host, CancellationToken ct);
    Task ShutdownAsync(CancellationToken ct);
}
