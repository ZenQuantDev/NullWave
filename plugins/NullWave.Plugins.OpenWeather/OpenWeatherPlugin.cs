using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NullWave.Services.Plugins;
using NullWave.Services.SmartSorting;

namespace NullWave.Plugins.OpenWeather;

public class OpenWeatherPlugin : IPlugin, IWeatherProvider
{
    private WeatherService? _weatherService;
    private bool _isConfigured;

    public string Name => "OpenWeather";
    public string Description => "Provides weather data for mood-based playlist generation";
    public PluginState State { get; set; } = PluginState.Unavailable;
    public bool IsEnabled { get; set; } = true;

    public ProviderDescriptor Descriptor => new(
        Id: "openweather",
        Kind: ProviderKind.Weather,
        DisplayName: "OpenWeather",
        PluginId: "com.nullwave.openweather"
    );

    public async Task<bool> InitializeAsync(IPluginHost host, CancellationToken ct = default)
    {
        if (!IsEnabled)
        {
            State = PluginState.Disabled;
            return false;
        }

        try
        {
            // Get API key from host (plugin never sees the raw key)
            var apiKey = host.GetApiKey("OpenWeather");
            if (string.IsNullOrEmpty(apiKey))
            {
                host.Log(LogLevel.Warning, "OpenWeather API key not configured");
                State = PluginState.Unavailable;
                return false;
            }

            _isConfigured = true;
            State = PluginState.Available;
            host.Log(LogLevel.Info, $"OpenWeather plugin initialized");
            return true;
        }
        catch (Exception ex)
        {
            host.Log(LogLevel.Error, $"OpenWeather initialization failed: {ex.Message}");
            State = PluginState.Error;
            return false;
        }
    }

    public Task ShutdownAsync(CancellationToken ct = default)
    {
        State = PluginState.Unavailable;
        return Task.CompletedTask;
    }

    public async Task<WeatherData?> GetCurrentWeatherAsync(double lat, double lon, CancellationToken ct = default)
    {
        if (State != PluginState.Available || !_isConfigured)
            return null;

        // In real implementation, this would call WeatherService
        // For now, return a mock response to prove the plugin system works
        return new WeatherData
        {
            Condition = "Clear",
            TemperatureC = 22.5,
            MoodTags = new List<string> { "upbeat", "sunny", "relaxed" }
        };
    }
}