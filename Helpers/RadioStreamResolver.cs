using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Serilog;

namespace NullWave.Helpers;

/// <summary>Resolves a station name/URL to a working stream URL via the open radio-browser.info directory.</summary>
public static class RadioStreamResolver
{
    public static async Task<string?> ResolveAsync(string stationName)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("NullWave/0.5");
            var q = Uri.EscapeDataString(stationName);
            var url = $"https://de1.api.radio-browser.info/json/stations/search?name={q}&limit=1&order=votes&reverse=true&hidebroken=true";
            var json = await http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            var first = doc.RootElement.EnumerateArray().FirstOrDefault();
            if (first.ValueKind == JsonValueKind.Object &&
                first.TryGetProperty("url_resolved", out var ur) &&
                !string.IsNullOrWhiteSpace(ur.GetString()))
                return ur.GetString();
        }
        catch (Exception ex) { Log.Warning(ex, "[Radio] radio-browser resolve failed for {Name}", stationName); }
        return null;
    }
}