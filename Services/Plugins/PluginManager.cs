using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace NullWave.Services.Plugins;

/// <summary>
/// Central registry for all plugins. Handles registration, lookup, and lifecycle.
/// </summary>
public class PluginManager
{
    private readonly List<IPlugin> _plugins = new();
    private readonly ILogger _logger;

    public IReadOnlyList<IPlugin> Plugins => _plugins.AsReadOnly();

    public PluginManager(ILogger? logger = null)
    {
        _logger = logger ?? Log.ForContext<PluginManager>();
    }

    /// <summary>Register a plugin. Duplicate names are ignored with a warning.</summary>
    public void Register<T>(T plugin) where T : IPlugin
    {
        if (_plugins.Any(p => p.Name == plugin.Name))
        {
            _logger.Warning("Plugin {PluginName} already registered, skipping", plugin.Name);
            return;
        }

        _plugins.Add(plugin);
        _logger.Information("Registered plugin: {PluginName} ({PluginType})",
            plugin.Name, typeof(T).Name);
    }

    /// <summary>
    /// Get the first enabled, non-error plugin of type <typeparamref name="T"/>.
    /// </summary>
    public T? Get<T>() where T : class, IPlugin
    {
        return _plugins
            .OfType<T>()
            .FirstOrDefault(p => p.IsEnabled && p.State == PluginState.Available);
    }

    /// <summary>
    /// Get all enabled, non-error plugins of type <typeparamref name="T"/>.
    /// </summary>
    public IEnumerable<T> GetAll<T>() where T : class, IPlugin
    {
        return _plugins
            .OfType<T>()
            .Where(p => p.IsEnabled && p.State != PluginState.Error);
    }

    /// <summary>Lookup a plugin by its <see cref="IPlugin.Name"/>.</summary>
    public IPlugin? GetByName(string name)
    {
        return _plugins.FirstOrDefault(p =>
            p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Initialize every enabled plugin. Failures are logged, not thrown.</summary>
    public async Task InitializeAllAsync(CancellationToken ct = default)
    {
        foreach (var plugin in _plugins.Where(p => p.IsEnabled))
        {
            try
            {
                _logger.Information("Initializing plugin: {PluginName}", plugin.Name);
                plugin.State = PluginState.Loading;
                var success = await plugin.InitializeAsync(ct);
                plugin.State = success ? PluginState.Available : PluginState.Error;

                if (!success)
                    _logger.Warning("Plugin {PluginName} initialization returned false", plugin.Name);
            }
            catch (Exception ex)
            {
                plugin.State = PluginState.Error;
                _logger.Error(ex, "Failed to initialize plugin: {PluginName}", plugin.Name);
            }
        }
    }

    /// <summary>Shut down every registered plugin. Errors are logged, not thrown.</summary>
    public async Task ShutdownAllAsync(CancellationToken ct = default)
    {
        foreach (var plugin in _plugins)
        {
            try
            {
                _logger.Information("Shutting down plugin: {PluginName}", plugin.Name);
                await plugin.ShutdownAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error shutting down plugin: {PluginName}", plugin.Name);
            }
        }
    }
}