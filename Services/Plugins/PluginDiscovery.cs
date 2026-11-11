using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using NullWave.Helpers;
using NullWave.Plugins.Contracts;
using Serilog;

namespace NullWave.Services.Plugins;

public class PluginDiscovery
{
    private readonly string _pluginsDirectory;
    private readonly Dictionary<string, AssemblyLoadContext> _contexts = new();
    private readonly TimeSpan _initTimeout;

    /// <summary>
    /// Shared JSON options for manifest deserialization.
    /// Case-insensitive property names and string enum conversion.
    /// </summary>
    public static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public PluginDiscovery(string pluginsDirectory, TimeSpan? initTimeout = null)
    {
        _pluginsDirectory = pluginsDirectory;
        _initTimeout = initTimeout ?? TimeSpan.FromSeconds(8);
        Directory.CreateDirectory(_pluginsDirectory);
    }

    public async Task<List<(PluginManifest Manifest, IPlugin Plugin)>> DiscoverAndLoadAsync(
        Func<PluginManifest, IPluginHost> hostFactory,
        CancellationToken ct = default,
        Func<PluginManifest, bool>? enabledState = null)
    {
        var loaded = new List<(PluginManifest, IPlugin)>();

        if (!Directory.Exists(_pluginsDirectory))
            return loaded;

        var pluginDirs = Directory.GetDirectories(_pluginsDirectory);

        foreach (var pluginDir in pluginDirs)
        {
            if (ct.IsCancellationRequested)
                break;

            try
            {
                var result = await LoadPluginFromDirectoryAsync(pluginDir, hostFactory, ct, enabledState);
                if (result != null)
                    loaded.Add(result.Value);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[PluginDiscovery] Failed to load plugin from {Dir}", pluginDir);
            }
        }

        return loaded;
    }

    private async Task<(PluginManifest Manifest, IPlugin Plugin)?> LoadPluginFromDirectoryAsync(
        string pluginDir,
        Func<PluginManifest, IPluginHost> hostFactory,
        CancellationToken ct,
        Func<PluginManifest, bool>? enabledState)
    {
        var manifestPath = Path.Combine(pluginDir, "plugin.json");
        if (!File.Exists(manifestPath))
        {
            Log.Debug("[PluginDiscovery] No plugin.json in {Dir}, skipping", pluginDir);
            return null;
        }

        // Parse manifest
        PluginManifest? manifest;
        try
        {
            var json = await File.ReadAllTextAsync(manifestPath, ct);
            manifest = JsonSerializer.Deserialize<PluginManifest>(json, ManifestJsonOptions);
        }
        catch (JsonException ex)
        {
            Log.Warning(ex, "[PluginDiscovery] Invalid plugin.json in {Dir}", pluginDir);
            return null;
        }

        if (manifest == null)
        {
            Log.Warning("[PluginDiscovery] plugin.json deserialized to null in {Dir}", pluginDir);
            return null;
        }

        // Validate manifest
        var validationError = ValidateManifest(manifest);
        if (validationError != null)
        {
            Log.Warning("[PluginDiscovery] Manifest validation failed for {PluginId}: {Error}",
                manifest.Id, validationError);
            return null;
        }

        if (!string.IsNullOrWhiteSpace(manifest.MinHostVersion) &&
            !AppVersionInfo.Satisfies(AppVersionInfo.Current, manifest.MinHostVersion))
        {
            Log.Warning("[PluginDiscovery] {PluginId} requires host {Min}, running {Current}; skipping",
                manifest.Id, manifest.MinHostVersion, AppVersionInfo.Current);
            return null;
        }

        // Locate DLL
        var dllName = manifest.EntryPoint.Split(',')[1].Trim();
        var dllPath = Path.Combine(pluginDir, $"{dllName}.dll");
        
        if (!File.Exists(dllPath))
        {
            Log.Warning("[PluginDiscovery] DLL not found for {PluginId}: {Path}", manifest.Id, dllPath);
            return null;
        }

        // Security check: ensure DLL path doesn't escape plugin directory
        var fullDllPath = Path.GetFullPath(dllPath);
        var fullPluginDir = Path.GetFullPath(pluginDir);
        if (!fullDllPath.StartsWith(fullPluginDir, StringComparison.OrdinalIgnoreCase))
        {
            Log.Warning("[PluginDiscovery] DLL path escapes plugin directory for {PluginId}", manifest.Id);
            return null;
        }

        // Load assembly in isolated context
        var context = new PluginLoadContext(dllPath);
        _contexts[manifest.Id] = context;

        var assembly = context.LoadFromAssemblyPath(fullDllPath);
        var pluginTypeName = manifest.EntryPoint.Split(',')[0].Trim();
        var pluginType = assembly.GetType(pluginTypeName);

        if (pluginType == null)
        {
            Log.Warning("[PluginDiscovery] Plugin type {Type} not found in {PluginId}",
                pluginTypeName, manifest.Id);
            return null;
        }

        if (!typeof(IPlugin).IsAssignableFrom(pluginType))
        {
            Log.Warning("[PluginDiscovery] Type {Type} does not implement IPlugin in {PluginId}",
                pluginTypeName, manifest.Id);
            return null;
        }

        var plugin = (IPlugin?)Activator.CreateInstance(pluginType);
        if (plugin == null)
        {
            Log.Warning("[PluginDiscovery] Failed to instantiate {Type} in {PluginId}",
                pluginTypeName, manifest.Id);
            return null;
        }

        if (enabledState != null)
            plugin.IsEnabled = enabledState(manifest);

        if (!plugin.IsEnabled)
        {
            plugin.State = PluginState.Disabled;
            Log.Information("[PluginDiscovery] Loaded disabled plugin: {Name} v{Version}",
                manifest.Name, manifest.Version);
            return (manifest, plugin);
        }

        // Initialize with timeout
        var pluginHost = hostFactory(manifest);
        
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_initTimeout);

        try
        {
            var initTask = plugin.InitializeAsync(pluginHost, timeoutCts.Token);
            var completedTask = await Task.WhenAny(initTask, Task.Delay(_initTimeout, timeoutCts.Token));

            if (completedTask != initTask)
            {
                Log.Warning("[PluginDiscovery] Plugin {Name} initialization timed out after {Seconds}s",
                    manifest.Name, _initTimeout.TotalSeconds);
                return null;
            }

            var success = await initTask;
            if (success)
            {
                Log.Information("[PluginDiscovery] Loaded plugin: {Name} v{Version}",
                    manifest.Name, manifest.Version);
                return (manifest, plugin);
            }
            else
            {
                Log.Information("[PluginDiscovery] Plugin {Name} initialized but returned inactive (state={State})",
                    manifest.Name, plugin.State);
                return null;
            }
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            Log.Warning("[PluginDiscovery] Plugin {Name} initialization timed out", manifest.Name);
            return null;
        }
    }

    private static string? ValidateManifest(PluginManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.Id))
            return "Id is required";
        
        if (string.IsNullOrWhiteSpace(manifest.Name))
            return "Name is required";
        
        if (string.IsNullOrWhiteSpace(manifest.Version))
            return "Version is required";
        
        if (string.IsNullOrWhiteSpace(manifest.EntryPoint))
            return "EntryPoint is required";
        
        if (!manifest.EntryPoint.Contains(','))
            return "EntryPoint must be in format 'Namespace.Type, AssemblyName'";
        
        if (manifest.Kinds == null || manifest.Kinds.Length == 0)
            return "At least one Kind is required";

        return null;
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
        // Prevent loading Contracts from plugin directory - use host's copy
        if (assemblyName.Name == "NullWave.Plugins.Contracts")
            return null;

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        if (path != null)
            return LoadFromAssemblyPath(path);

        return null;
    }
}