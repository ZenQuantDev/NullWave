using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using NullWave.Helpers;
using NullWave.Services;
using Serilog;

namespace NullWave.Services.SmartSorting;

public class WeatherInfo
{
    public string Condition { get; set; } = "Unknown";
    public double TemperatureC { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTime FetchedAt { get; set; } = DateTime.UtcNow;
}

public class WeatherService
{
    private readonly HttpClient _http = new();
    private readonly KeyStoreService _keyStore;
    private readonly string _cachePath;
    private WeatherInfo? _cachedWeather;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);

    public WeatherService(KeyStoreService keyStore)
    {
        _keyStore = keyStore;
        _cachePath = Path.Combine(NullWavePaths.DataDir, "weather_cache.json");
        LoadCacheFromDisk();
    }

    private void LoadCacheFromDisk()
    {
        try
        {
            if (File.Exists(_cachePath))
            {
                var json = File.ReadAllText(_cachePath);
                _cachedWeather = JsonSerializer.Deserialize<WeatherInfo>(json);
                if (_cachedWeather != null)
                {
                    Log.Debug("[WeatherService] Loaded weather cache from disk (fetched at {Time})", _cachedWeather.FetchedAt);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[WeatherService] Failed to load weather cache from disk");
            _cachedWeather = null;
        }
    }

    private void SaveCacheToDisk()
    {
        try
        {
            if (_cachedWeather != null)
            {
                var json = JsonSerializer.Serialize(_cachedWeather);
                File.WriteAllText(_cachePath, json);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[WeatherService] Failed to save weather cache to disk");
        }
    }

    private string? GetApiKey() => _keyStore.GetKey("OpenWeather");
    public bool IsConfigured => !string.IsNullOrEmpty(GetApiKey());

    private bool IsCacheValid => _cachedWeather != null && (DateTime.UtcNow - _cachedWeather.FetchedAt) < CacheDuration;

    public async Task<WeatherInfo?> GetWeatherAsync(double latitude, double longitude, bool forceRefresh = false)
    {
        if (!forceRefresh && IsCacheValid)
        {
            return _cachedWeather;
        }

        var apiKey = GetApiKey();
        if (string.IsNullOrEmpty(apiKey))
        {
            Log.Warning("[WeatherService] OpenWeather API key not configured");
            return null;
        }

        try
        {
            var url = $"https://api.openweathermap.org/data/2.5/weather?lat={latitude}&lon={longitude}&appid={apiKey}&units=metric";
            var response = await _http.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var weather = root.GetProperty("weather")[0];
            var main = root.GetProperty("main");

            _cachedWeather = new WeatherInfo
            {
                Condition = weather.GetProperty("main").GetString() ?? "Unknown",
                TemperatureC = main.GetProperty("temp").GetDouble(),
                Description = weather.GetProperty("description").GetString() ?? "",
                FetchedAt = DateTime.UtcNow
            };

            SaveCacheToDisk();

            Log.Information("[WeatherService] Weather fetched: {Condition}, {Temp}°C",
                _cachedWeather.Condition, _cachedWeather.TemperatureC);

            return _cachedWeather;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[WeatherService] Failed to fetch weather");
            // Return stale cache on network failure rather than null
            return _cachedWeather; 
        }
    }
}