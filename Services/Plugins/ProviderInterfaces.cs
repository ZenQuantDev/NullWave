using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NullWave.Models;
using NullWave.Plugins.Contracts;

namespace NullWave.Services.Plugins;

public interface IDownloadProvider : IProvider
{
    bool SupportsUrl(string url);
    Task<DownloadResult> DownloadAsync(string url, DownloadOptions options, CancellationToken ct = default);
}

public interface IMetadataProvider : IProvider
{
    Task<TrackMetadata?> FetchMetadataAsync(string identifier, TrackSource source, CancellationToken ct = default);
}

public interface IAIProvider : IProvider
{
    Task<IEnumerable<Track>> RankTracksAsync(IEnumerable<Track> tracks, string context, CancellationToken ct = default);
}

public interface IWeatherProvider : IProvider
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

/// <summary>
/// Extension to build a PluginManifest from a built-in IPlugin/IProvider.
/// Lives here (not in Contracts) because it references IProvider which is app-internal.
/// </summary>
public static class PluginManifestExtensions
{
    public static PluginManifest FromBuiltIn(IPlugin p) => new(
        p is IProvider ip ? ip.Descriptor.Id : p.Name,
        p.Name,
        "NullWave",
        "built-in",
        p.Description,
        p is IProvider q ? new[] { q.Descriptor.Kind } : Array.Empty<ProviderKind>(),
        true,
        PluginAvailability.Installed);
}