using System;
using System.Collections.Generic;
using System.Globalization;
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
    private readonly string[] _pluginRoots;
    private readonly Dictionary<string, AssemblyLoadContext> _contexts = new();
    private readonly TimeSpan _initTimeout;

    /// <summary>
    /// Shared JSON options for manifest deserialization.
    /// Case-insensitive property names and string enum conversion.
    /// </summary>
    public static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter() }
    };

    public PluginDiscovery(string pluginsDirectory, TimeSpan? initTimeout = null)
        : this(new[] { pluginsDirectory }, initTimeout)
    {
    }

    /// <summary>Roots are probed in order; the first root declaring a manifest Id owns it.</summary>
    public PluginDiscovery(IEnumerable<string> pluginRoots, TimeSpan? initTimeout = null)
    {
        _pluginRoots = pluginRoots.Where(root => !string.IsNullOrWhiteSpace(root)).ToArray();
        _initTimeout = initTimeout ?? PluginManager.DefaultInitTimeout;
        foreach (var root in _pluginRoots)
        {
            try
            {
                Directory.CreateDirectory(root);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[PluginDiscovery] Could not create plugin root {Root}", root);
            }
        }
    }

    /// <summary>Ordered candidate directories. Missing or unreadable roots are skipped.</summary>
    public static IEnumerable<string> EnumeratePluginDirectories(IEnumerable<string> roots)
    {
        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root)) continue;

            string[] directories;
            try
            {
                directories = Directory.GetDirectories(root);
            }
            catch (DirectoryNotFoundException)
            {
                continue;
            }
            catch (UnauthorizedAccessException ex)
            {
                Log.Debug(ex, "[PluginDiscovery] Cannot enumerate plugin root {Root}", root);
                continue;
            }

            foreach (var directory in directories.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                yield return directory;
        }
    }

    public async Task<List<(PluginManifest Manifest, IPlugin Plugin)>> DiscoverAndLoadAsync(
        Func<PluginManifest, IPluginHost> hostFactory,
        CancellationToken ct = default,
        Func<PluginManifest, bool>? enabledState = null)
    {
        var loaded = new List<(PluginManifest, IPlugin)>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pluginDir in EnumeratePluginDirectories(_pluginRoots))
        {
            if (ct.IsCancellationRequested)
                break;

            try
            {
                var result = await LoadPluginFromDirectoryAsync(
                    pluginDir, hostFactory, ct, enabledState, seenIds);
                if (result != null)
                    loaded.Add(result.Value);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
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
        Func<PluginManifest, bool>? enabledState,
        HashSet<string> seenIds)
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

        if (!seenIds.Add(manifest.Id))
        {
            Log.Information("[PluginDiscovery] {PluginId} already claimed by an earlier root; skipping {Dir}",
                manifest.Id, pluginDir);
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
        var entryParts = manifest.EntryPoint.Split(',');
        var pluginTypeName = entryParts[0].Trim();
        var dllName = entryParts[1].Trim();
        var dllPath = Path.Combine(pluginDir, $"{dllName}.dll");
        
        if (!File.Exists(dllPath))
        {
            Log.Warning("[PluginDiscovery] DLL not found for {PluginId}: {Path}", manifest.Id, dllPath);
            return null;
        }

        // Require a path-component boundary so sibling directories with a shared prefix do not pass.
        var fullDllPath = Path.GetFullPath(dllPath);
        var fullPluginDir = Path.GetFullPath(pluginDir);
        var dirPrefix = fullPluginDir.EndsWith(
            Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? fullPluginDir
            : fullPluginDir + Path.DirectorySeparatorChar;
        var pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!fullDllPath.StartsWith(dirPrefix, pathComparison))
        {
            Log.Warning("[PluginDiscovery] DLL path escapes plugin directory for {PluginId}", manifest.Id);
            return null;
        }

        // Load assembly in isolated context
        var context = new PluginLoadContext(dllPath);
        _contexts[manifest.Id] = context;

        IPlugin? plugin = null;
        void AbandonContext()
        {
            if (_contexts.TryGetValue(manifest.Id, out var current) &&
                ReferenceEquals(current, context))
            {
                _contexts.Remove(manifest.Id);
            }

            try
            {
                context.Unload();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[PluginDiscovery] Unload failed for {PluginId}", manifest.Id);
            }
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_initTimeout);
        Task<bool>? initTask = null;
        try
        {
            var assembly = context.LoadFromAssemblyPath(fullDllPath);
            var pluginType = assembly.GetType(pluginTypeName);
            if (pluginType == null)
            {
                Log.Warning("[PluginDiscovery] Plugin type {Type} not found in {PluginId}",
                    pluginTypeName, manifest.Id);
                AbandonContext();
                return null;
            }

            if (!typeof(IPlugin).IsAssignableFrom(pluginType))
            {
                Log.Warning("[PluginDiscovery] Type {Type} does not implement IPlugin in {PluginId}",
                    pluginTypeName, manifest.Id);
                AbandonContext();
                return null;
            }

            plugin = (IPlugin?)Activator.CreateInstance(pluginType);
            if (plugin == null)
            {
                Log.Warning("[PluginDiscovery] Failed to instantiate {Type} in {PluginId}",
                    pluginTypeName, manifest.Id);
                AbandonContext();
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

            var pluginHost = hostFactory(manifest);
            initTask = plugin.InitializeAsync(pluginHost, timeoutCts.Token);

            var success = await initTask.WaitAsync(_initTimeout, ct);
            if (!success)
            {
                Log.Information("[PluginDiscovery] Plugin {Name} initialized but returned inactive (state={State})",
                    manifest.Name, plugin.State);
                AbandonContext();
                return null;
            }

            Log.Information("[PluginDiscovery] Loaded plugin: {Name} v{Version}",
                manifest.Name, manifest.Version);
            return (manifest, plugin);
        }
        catch (TimeoutException)
        {
            timeoutCts.Cancel();
            Log.Warning("[PluginDiscovery] Plugin {Name} initialization timed out after {Seconds}s",
                manifest.Name, _initTimeout.TotalSeconds);
            ObserveLateFailure(initTask);
            AbandonContext();
            return null;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            timeoutCts.Cancel();
            Log.Warning("[PluginDiscovery] Plugin {Name} initialization timed out", manifest.Name);
            ObserveLateFailure(initTask);
            AbandonContext();
            return null;
        }
        catch (OperationCanceledException)
        {
            ObserveLateFailure(initTask);
            AbandonContext();
            throw;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[PluginDiscovery] Failed to load {PluginId} from {Dir}", manifest.Id, pluginDir);
            ObserveLateFailure(initTask);
            AbandonContext();
            return null;
        }
    }

    private static void ObserveLateFailure(Task? task)
    {
        if (task is { IsFaulted: true })
        {
            _ = task.Exception;
            return;
        }

        if (task is { IsCompleted: false })
        {
            _ = task.ContinueWith(
                completed => _ = completed.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
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
        
        var entryParts = manifest.EntryPoint.Split(',');
        if (entryParts.Length != 2 ||
            string.IsNullOrWhiteSpace(entryParts[0]) ||
            string.IsNullOrWhiteSpace(entryParts[1]))
        {
            return "EntryPoint must be 'Namespace.Type, AssemblyName'";
        }
        
        if (manifest.Kinds == null || manifest.Kinds.Length == 0)
            return "At least one Kind is required";

        if (manifest.Permissions == null || manifest.Permissions.ApiKeys == null)
            return "Permissions and apiKeys must not be null";

        if (!string.IsNullOrWhiteSpace(manifest.MinHostVersion) &&
            !IsVersionRequirement(manifest.MinHostVersion))
        {
            return "MinHostVersion must be a major.minor.patch version with an optional prerelease suffix";
        }

        return null;
    }

    private static bool IsVersionRequirement(string value)
    {
        var versionParts = value.Split('-', 2);
        if (versionParts.Length == 2 && string.IsNullOrWhiteSpace(versionParts[1]))
            return false;

        var coreVersion = versionParts[0];
        var parts = coreVersion.Split('.');
        return parts.Length == 3 &&
               parts.All(part => int.TryParse(
                   part, NumberStyles.None, CultureInfo.InvariantCulture, out _));
    }

    public void UnloadAll()
    {
        foreach (var (pluginId, context) in _contexts.ToArray())
        {
            try
            {
                context.Unload();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[PluginDiscovery] Failed to unload context for {PluginId}", pluginId);
            }
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