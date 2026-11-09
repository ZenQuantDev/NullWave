using System.Threading;
using System.Threading.Tasks;

namespace NullWave.Plugins.Contracts;

/// <summary>
/// Base interface for all NullWave plugins.
/// Plugins implement this to participate in the plugin lifecycle.
/// </summary>
public interface IPlugin
{
    /// <summary>Unique plugin identifier (matches manifest Id)</summary>
    string Id { get; }

    /// <summary>Human-readable display name</summary>
    string Name { get; }

    /// <summary>Short description of what the plugin does</summary>
    string Description { get; }

    /// <summary>Current lifecycle state</summary>
    PluginState State { get; set; }

    /// <summary>Whether the plugin is enabled by the user</summary>
    bool IsEnabled { get; set; }

    /// <summary>
    /// Called once after the plugin assembly is loaded.
    /// Returns true if initialization succeeded, false if the plugin should remain inactive.
    /// </summary>
    Task<bool> InitializeAsync(IPluginHost host, CancellationToken ct = default);

    /// <summary>Called when the plugin is being unloaded or the app is shutting down.</summary>
    Task ShutdownAsync(CancellationToken ct = default);
}