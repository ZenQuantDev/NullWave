using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using NullWave.Models.Plugins;
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

    public async Task<List<(PluginManifest Manifest, IPlugin Plugin)>> DiscoverAndLoadAsync(IPluginHost host)
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

                var dllPath = Path.Combine(pluginDir, $"{manifest.Id}.dll");
                if (!File.Exists(dllPath))
                {
                    Log.Warning("[PluginDiscovery] DLL not found for {PluginId}", manifest.Id);
                    continue;
                }

                var context = new PluginLoadContext(dllPath);
                _contexts[manifest.Id] = context;

                var assembly = context.LoadFromAssemblyPath(dllPath);
                var pluginType = assembly.GetType(manifest.EntryPoint.Split(',')[0]);
                if (pluginType == null)
                {
                    Log.Warning("[PluginDiscovery] Plugin type {Type} not found in {PluginId}", 
                        manifest.EntryPoint, manifest.Id);
                    continue;
                }

                var plugin = (IPlugin?)Activator.CreateInstance(pluginType);
                if (plugin == null) continue;

                var success = await plugin.InitializeAsync(host, default);
                if (success)
                {
                    loaded.Add((manifest, plugin));
                    Log.Information("[PluginDiscovery] Loaded plugin: {Name} v{Version}", 
                        manifest.Name, manifest.Version);
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