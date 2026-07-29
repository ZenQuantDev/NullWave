using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NullWave.Models;

namespace NullWave.Services.Plugins;

public interface IPlugin
{
    string Name { get; }
    string Description { get; }
    PluginState State { get; set; }
    bool IsEnabled { get; set; }
    Task<bool> InitializeAsync(CancellationToken ct = default);
    Task ShutdownAsync(CancellationToken ct = default);
}

public interface IDownloadProvider : IPlugin
{
    bool SupportsUrl(string url);
    Task<DownloadResult> DownloadAsync(string url, DownloadOptions options, CancellationToken ct = default);
}

public interface IMetadataProvider : IPlugin
{
    Task<TrackMetadata?> FetchMetadataAsync(string identifier, TrackSource source, CancellationToken ct = default);
}

public interface IAIProvider : IPlugin
{
    Task<IEnumerable<Track>> RankTracksAsync(IEnumerable<Track> tracks, string context, CancellationToken ct = default);
}

public interface IWeatherProvider : IPlugin
{
    Task<WeatherData?> GetCurrentWeatherAsync(double lat, double lon, CancellationToken ct = default);
}

public class DownloadOptions
{
    public string Format { get; set; } = "mp3";
    public string Quality { get; set; } = "best";
    public string? OutputDirectory { get; set; }
}

public class DownloadResult
{
    public bool Success { get; init; }
    public string? FilePath { get; init; }
    public string? ErrorMessage { get; init; }

    public static DownloadResult Failed(string error) =>
        new() { Success = false, ErrorMessage = error };

    public static DownloadResult Succeeded(string path) =>
        new() { Success = true, FilePath = path };
}

public class TrackMetadata
{
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string? AlbumArtUrl { get; set; }
    public List<string> Tags { get; set; } = new();
}

public class WeatherData
{
    public string Condition { get; set; } = string.Empty;
    public double TemperatureC { get; set; }
    public List<string> MoodTags { get; set; } = new();
}