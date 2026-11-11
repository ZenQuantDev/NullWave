using NullWave.Models;
using NullWave.Services.Plugins;
using Xunit;

namespace NullWave.Tests;

public class PluginEnabledStateTests
{
    [Fact]
    public void Absent_entry_defaults_to_enabled()
    {
        Assert.True(PluginEnabledState.Resolve(new Preferences(), "radiobrowser"));
    }

    [Fact]
    public void Persist_creates_entry_keyed_by_id()
    {
        var prefs = new Preferences();

        PluginEnabledState.Persist(prefs, "radiobrowser", false);

        Assert.Single(prefs.PluginConfigs);
        Assert.Equal("radiobrowser", prefs.PluginConfigs[0].Name);
        Assert.False(PluginEnabledState.Resolve(prefs, "radiobrowser"));
    }

    [Fact]
    public void Persist_updates_existing_entry_without_duplicating()
    {
        var prefs = new Preferences();
        PluginEnabledState.Persist(prefs, "radiobrowser", false);

        PluginEnabledState.Persist(prefs, "radiobrowser", true);

        Assert.Single(prefs.PluginConfigs);
        Assert.True(PluginEnabledState.Resolve(prefs, "radiobrowser"));
    }

    [Fact]
    public void Resolve_is_case_insensitive()
    {
        var prefs = new Preferences();
        PluginEnabledState.Persist(prefs, "RadioBrowser", false);

        Assert.False(PluginEnabledState.Resolve(prefs, "radiobrowser"));
    }

    [Fact]
    public void Disabled_choice_survives_a_preferences_round_trip()
    {
        var prefs = new Preferences();
        PluginEnabledState.Persist(prefs, "radiobrowser", false);

        var json = System.Text.Json.JsonSerializer.Serialize(prefs);
        var restored = System.Text.Json.JsonSerializer.Deserialize<Preferences>(json)!;

        Assert.False(PluginEnabledState.Resolve(restored, "radiobrowser"));
    }
}
