using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using NullWave.Services;
using Serilog;

namespace NullWave.Services.SmartSorting;

public class WeatherInfo
{
    public string Condition { get; init; } = "Unknown";
    public double TemperatureC { get; init; }
    public string Description { get; init; } = string.Empty;
    public DateTime FetchedAt { get; init; } = DateTime.UtcNow;
}

public class WeatherService
{
    private readonly HttpClient _http = new();
    private readonly KeyStoreService _keyStore;
    private WeatherInfo? _cachedWeather;
    private DateTime _lastFetch = DateTime.MinValue;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);

    public WeatherService(KeyStoreService keyStore)
    {
        _keyStore = keyStore;
    }

    /// <summary>
    /// Reads the API key fresh from the key store on every call,
    /// so adding the key in Settings takes effect immediately without restart.
    /// </summary>
    private string? GetApiKey() => _keyStore.GetKey("OpenWeather");

    public bool IsConfigured => !string.IsNullOrEmpty(GetApiKey());

    public async Task<WeatherInfo?> GetWeatherAsync(double latitude, double longitude, bool forceRefresh = false)
    {
        if (!forceRefresh && _cachedWeather != null &&
            DateTime.UtcNow - _lastFetch < CacheDuration)
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

            _lastFetch = DateTime.UtcNow;
            Log.Information("[WeatherService] Weather fetched: {Condition}, {Temp}°C",
                _cachedWeather.Condition, _cachedWeather.TemperatureC);

            return _cachedWeather;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[WeatherService] Failed to fetch weather");
            return null;
        }
    }
}