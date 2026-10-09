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
    /// <summary>How long one plugin may take to initialize before the app carries on without it.</summary>
    public static readonly TimeSpan DefaultInitTimeout = TimeSpan.FromSeconds(8);

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

    /// <summary>
    /// Initialize every enabled plugin at the same time. Total start-up time is the slowest plugin,
    /// not the sum. A plugin that does not finish within <paramref name="perPluginTimeout"/> is marked
    /// Error and the app carries on without it. Failures are logged, not thrown.
    /// Each plugin's own start-up code runs on a pool thread, so a plugin that blocks (for example
    /// while waiting for a process) cannot freeze the window.
    /// </summary>
    public async Task InitializeAllAsync(CancellationToken ct = default, TimeSpan? perPluginTimeout = null)
    {
        var timeout = perPluginTimeout ?? DefaultInitTimeout;
        var enabled = _plugins.Where(p => p.IsEnabled).ToList();
        await Task.WhenAll(enabled.Select(p => InitializeOneAsync(p, timeout, ct)));
    }

    private async Task InitializeOneAsync(IPlugin plugin, TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            _logger.Information("Initializing plugin: {PluginName}", plugin.Name);
            plugin.State = PluginState.Loading;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);

            var init = Task.Run(() => plugin.InitializeAsync(cts.Token));
            var finished = await Task.WhenAny(init, Task.Delay(timeout, cts.Token));

            if (!ReferenceEquals(finished, init))
            {
                // The plugin ignored its cancellation token (or app shutdown cancelled us).
                // Observe any late failure so it does not surface as an unobserved task exception.
                _ = init.ContinueWith(t => { _ = t.Exception; },
                    CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);

                if (ct.IsCancellationRequested)
                {
                    plugin.State = PluginState.Unavailable;
                    return;
                }

                plugin.State = PluginState.Error;
                _logger.Warning("Plugin {PluginName} did not finish initializing within {Seconds:F1}s; continuing without it",
                    plugin.Name, timeout.TotalSeconds);
                return;
            }

            var success = await init;
            cts.Cancel();   // releases the timeout timer
            plugin.State = success ? PluginState.Available : PluginState.Error;

            if (!success)
                _logger.Information("Plugin {Name} initialized without optional configuration - its features stay off until configured", plugin.Name);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // The plugin honoured the timeout token.
            plugin.State = PluginState.Error;
            _logger.Warning("Plugin {PluginName} did not finish initializing within {Seconds:F1}s; continuing without it",
                plugin.Name, timeout.TotalSeconds);
        }
        catch (OperationCanceledException)
        {
            plugin.State = PluginState.Unavailable;
        }
        catch (Exception ex)
        {
            plugin.State = PluginState.Error;
            _logger.Error(ex, "Failed to initialize plugin: {PluginName}", plugin.Name);
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