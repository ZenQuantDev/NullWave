using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;

namespace NullWave.Helpers;

public static class BadgeCatalog
{
    private static readonly Dictionary<string, BadgeDefinition> _definitions = new();
    private static bool _loaded = false;

    public static IReadOnlyDictionary<string, BadgeDefinition> Definitions => _definitions;

    public static void Load()
    {
        if (_loaded) return;
        
        try
        {
            // Load embedded catalog
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "NullWave.Assets.Badges.badges.catalog.json";
            
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                Log.Warning("[BadgeCatalog] Could not find embedded resource {Name}", resourceName);
                return;
            }

            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            var catalog = JsonSerializer.Deserialize<BadgeCatalogFile>(json);
            
            if (catalog?.Badges != null)
            {
                foreach (var badge in catalog.Badges)
                {
                    _definitions[badge.Id] = badge;
                }
                Log.Information("[BadgeCatalog] Loaded {Count} badge definitions", _definitions.Count);
            }

            // Dev-mode override: check for user-side override file
            if (Environment.GetEnvironmentVariable("NULLWAVE_DEV") == "1" || 
                Environment.GetCommandLineArgs().Any(a => a == "--dev"))
            {
                var overridePath = Path.Combine(NullWavePaths.DataDir, "badges-override.json");
                if (File.Exists(overridePath))
                {
                    try
                    {
                        var overrideJson = File.ReadAllText(overridePath);
                        var overrideCatalog = JsonSerializer.Deserialize<BadgeCatalogFile>(overrideJson);
                        if (overrideCatalog?.Badges != null)
                        {
                            foreach (var badge in overrideCatalog.Badges)
                            {
                                _definitions[badge.Id] = badge;
                            }
                            Log.Information("[BadgeCatalog] Loaded {Count} override badges from {Path}", 
                                overrideCatalog.Badges.Count, overridePath);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "[BadgeCatalog] Failed to load override file");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[BadgeCatalog] Failed to load catalog");
        }
        finally
        {
            _loaded = true;
        }
    }

    public static BadgeDefinition? GetById(string id) => 
        _definitions.TryGetValue(id, out var def) ? def : null;

    public static IEnumerable<BadgeDefinition> GetGranted() => 
        _definitions.Values.Where(d => d.Kind == BadgeKind.Granted);

    public static IEnumerable<BadgeDefinition> GetComputed() => 
        _definitions.Values.Where(d => d.Kind == BadgeKind.Computed);

    private record BadgeCatalogFile
    {
        [JsonPropertyName("badges")]
        public List<BadgeDefinition>? Badges { get; init; }
    }
}
