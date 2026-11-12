using System;
using System.Threading;
using System.Threading.Tasks;
using NullWave.Plugins.Contracts;

namespace NullWave.Plugins.OpenWeather;

public class OpenWeatherPlugin : IPlugin
{
    private IPluginHost? _host;

    public string Id => "com.nullwave.openweather";
    public string Name => "OpenWeather";
    public string Description => "Provides weather data for mood-based playlist generation";
    public PluginState State { get; set; } = PluginState.Unavailable;
    public bool IsEnabled { get; set; } = true;

    public async Task<bool> InitializeAsync(IPluginHost host, CancellationToken ct = default)
    {
        _host = host;

        if (!IsEnabled)
        {
            State = PluginState.Disabled;
            host.Log(LogLevel.Info, "OpenWeather plugin disabled by user preference");
            return false;
        }

        try
        {
            var apiKey = host.GetApiKey("OpenWeather");
            if (string.IsNullOrEmpty(apiKey))
            {
                host.Log(LogLevel.Warning, "OpenWeather API key not configured");
                State = PluginState.Unavailable;
                return false;
            }

            State = PluginState.Available;
            host.Log(LogLevel.Info, "OpenWeather plugin initialized successfully");
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
        _host?.Log(LogLevel.Info, "OpenWeather plugin shut down");
        return Task.CompletedTask;
    }
}