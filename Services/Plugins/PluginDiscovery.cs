using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NullWave.Plugins.Contracts;
using Serilog;

namespace NullWave.Services.Plugins;

public class PluginDiscovery
{
    private readonly string _pluginsDirectory;
    private readonly Dictionary<string, AssemblyLoadContext> _contexts = new();

    public PluginDiscovery(string pluginsDirectory)
    {
        _pluginsDirectory = pluginsDirectory;
        Directory.CreateDirectory(_pluginsDirectory);
    }

    public async Task<List<(PluginManifest Manifest, IPlugin Plugin)>> DiscoverAndLoadAsync(Func<PluginManifest, IPluginHost> hostFactory)
    {
        var loaded = new List<(PluginManifest, IPlugin)>();

        if (!Directory.Exists(_pluginsDirectory))
            return loaded;

        var pluginDirs = Directory.GetDirectories(_pluginsDirectory);

        foreach (var pluginDir in pluginDirs)
        {
            try
            {
                var manifestPath = Path.Combine(pluginDir, "plugin.json");
                if (!File.Exists(manifestPath))
                    continue;

                var json = await File.ReadAllTextAsync(manifestPath);
                var manifest = JsonSerializer.Deserialize<PluginManifest>(json);
                if (manifest == null) continue;

                var dllName = manifest.EntryPoint.Split(',')[1].Trim();
                var dllPath = Path.Combine(pluginDir, $"{dllName}.dll");
                if (!File.Exists(dllPath))
                {
                    Log.Warning("[PluginDiscovery] DLL not found for {PluginId}: {Path}", manifest.Id, dllPath);
                    continue;
                }

                var context = new PluginLoadContext(dllPath);
                _contexts[manifest.Id] = context;

                var assembly = context.LoadFromAssemblyPath(dllPath);
                var pluginTypeName = manifest.EntryPoint.Split(',')[0];
                var pluginType = assembly.GetType(pluginTypeName);

                if (pluginType == null)
                {
                    Log.Warning("[PluginDiscovery] Plugin type {Type} not found in {PluginId}",
                        pluginTypeName, manifest.Id);
                    continue;
                }

                var plugin = (IPlugin?)Activator.CreateInstance(pluginType);
                if (plugin == null) continue;

                // Create a per-plugin host instance
                var pluginHost = hostFactory(manifest);

                var success = await plugin.InitializeAsync(pluginHost, default);
                if (success)
                {
                    loaded.Add((manifest, plugin));
                    Log.Information("[PluginDiscovery] Loaded plugin: {Name} v{Version}",
                        manifest.Name, manifest.Version);
                }
                else
                {
                    Log.Information("[PluginDiscovery] Plugin {Name} initialized but returned inactive (state={State})",
                        manifest.Name, plugin.State);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[PluginDiscovery] Failed to load plugin from {Dir}", pluginDir);
            }
        }

        return loaded;
    }

    public void UnloadAll()
    {
        foreach (var context in _contexts.Values)
        {
            context.Unload();
        }
        _contexts.Clear();
    }
}

internal class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    public PluginLoadContext(string pluginPath) : base(isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(pluginPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        if (path != null)
            return LoadFromAssemblyPath(path);

        return null;
    }
}