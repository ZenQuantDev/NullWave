using System;
using System.Linq;
using NullWave.Models;

namespace NullWave.Services.Plugins;

/// <summary>
/// Persisted on/off state for plugins that have no dedicated preference flag.
/// Keyed by manifest Id in Preferences.PluginConfigs.
/// </summary>
public static class PluginEnabledState
{
    public static bool Resolve(Preferences prefs, string pluginId) =>
        prefs.PluginConfigs
            .FirstOrDefault(config => string.Equals(config.Name, pluginId, StringComparison.OrdinalIgnoreCase))
            ?.Enabled ?? true;

    public static void Persist(Preferences prefs, string pluginId, bool enabled)
    {
        var config = prefs.PluginConfigs
            .FirstOrDefault(item => string.Equals(item.Name, pluginId, StringComparison.OrdinalIgnoreCase));
        if (config == null)
        {
            config = new PluginConfig { Name = pluginId };
            prefs.PluginConfigs.Add(config);
        }

        config.Enabled = enabled;
    }
}
