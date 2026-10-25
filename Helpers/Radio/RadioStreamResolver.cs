using System.Threading.Tasks;
using NullWave.Services;

namespace NullWave.Helpers;

/// <summary>Resolves a station name to a working stream URL via the shared radio-browser client.</summary>
public static class RadioStreamResolver
{
    public static Task<string?> ResolveAsync(string stationName) => RadioBrowserProvider.ResolveStreamAsync(stationName);
}