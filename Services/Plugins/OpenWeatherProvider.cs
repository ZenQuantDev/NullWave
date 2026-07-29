using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NullWave.Helpers;
using NullWave.Models;
using NullWave.Services.SmartSorting;
using Serilog;

namespace NullWave.Services.Plugins;

/// <summary>
/// Wraps WeatherService as an optional plugin. When disabled or unconfigured,
/// GetCurrentWeatherAsync returns null and MoodPlaylistService falls back to
/// generic defaults.
/// </summary>
public class OpenWeatherProvider : IWeatherProvider
{
    private readonly WeatherService _inner;
    private readonly PreferencesService _prefs;

    public string Name => "OpenWeather";
    public string Description => "Fetches local weather for AI mood playlists";
    public PluginState State { get; set; } = PluginState.Unavailable;
    public bool IsEnabled { get; set; } = true;

    public OpenWeatherProvider(WeatherService inner, PreferencesService prefs)
    {
        _inner = inner;
        _prefs = prefs;
        IsEnabled = prefs.Current.EnableOpenWeather;
    }

    public Task<bool> InitializeAsync(CancellationToken ct = default)
    {
        if (!IsEnabled)
        {
            State = PluginState.Disabled;
            return Task.FromResult(false);
        }

        if (!_inner.IsConfigured)
        {
            State = PluginState.Unavailable;
            Log.Information("[{Name}] No API key configured — weather features will use fallback", Name);
            return Task.FromResult(false);
        }

        State = PluginState.Available;
        Log.Information("[{Name}] Ready — API key configured", Name);
        return Task.FromResult(true);
    }

    public Task ShutdownAsync(CancellationToken ct = default)
    {
        State = PluginState.Unavailable;
        return Task.CompletedTask;
    }

    public async Task<WeatherData?> GetCurrentWeatherAsync(double lat, double lon, CancellationToken ct = default)
    {
        if (!IsEnabled || State != PluginState.Available)
            return null;

        var info = await _inner.GetWeatherAsync(lat, lon, forceRefresh: false);
        if (info == null) return null;

        return new WeatherData
        {
            Condition = info.Condition,
            TemperatureC = info.TemperatureC,
            MoodTags = WeatherMoodMap.GetMoodTags(info.Condition, info.TemperatureC, DateTime.Now.Hour).ToList()
        };
    }
}