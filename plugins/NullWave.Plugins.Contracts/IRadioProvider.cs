using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NullWave.Plugins.Contracts;

/// <summary>
/// Provides radio station discovery and streaming.
/// Inherits from IPlugin (not IProvider) because IProvider is app-internal.
/// </summary>
public interface IRadioProvider : IPlugin
{
    /// <summary>Search for radio stations by name or query.</summary>
    Task<List<RadioStationInfo>> SearchStationsAsync(string query, int limit = 50, CancellationToken ct = default);

    /// <summary>Get popular/top radio stations.</summary>
    Task<List<RadioStationInfo>> GetTopStationsAsync(int limit = 50, CancellationToken ct = default);

    /// <summary>
    /// Click-free lookup of one station by directory UUID.
    /// Used for stream repair; unlike ResolveStreamUrlAsync this does NOT register a play.
    /// </summary>
    Task<RadioStationInfo?> GetStationAsync(string stationId, CancellationToken ct = default);

    /// <summary>Resolve a station ID to an actual stream URL.</summary>
    Task<string?> ResolveStreamUrlAsync(string stationId, CancellationToken ct = default);
}

/// <summary>
/// Represents a radio station with metadata.
/// </summary>
public sealed record RadioStationInfo(
    string Id,
    string Name,
    string? Genre,
    string? Country,
    string? Language,
    string? StreamUrl,
    string? LogoUrl,
    int ClickCount = 0,
    int Votes = 0
);