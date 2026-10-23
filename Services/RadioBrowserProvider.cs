using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NullWave.Helpers;
using Serilog;

namespace NullWave.Services;

public class RadioBrowserProvider
{
    private const string FallbackHost = "de1.api.radio-browser.info";
    private static readonly HttpClient _http = CreateHttpClient();
    private static readonly SemaphoreSlim _serverLock = new(1, 1);
    private static string[]? _servers;

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var version = typeof(RadioBrowserProvider).Assembly.GetName().Version?.ToString(3) ?? "0";
        client.DefaultRequestHeaders.UserAgent.ParseAdd("NullWave/" + version);
        return client;
    }

    public Task<List<RadioChannel>> SearchAsync(string query, int limit = 20, CancellationToken ct = default) =>
        FetchAsync($"/stations/search?name={Uri.EscapeDataString(query)}&limit={limit}&hidebroken=true", "Search", ct);

    public Task<List<RadioChannel>> BrowseByGenreAsync(string genre, int limit = 20, CancellationToken ct = default) =>
        FetchAsync($"/stations/bytag/{Uri.EscapeDataString(genre)}?limit={limit}&hidebroken=true&order=clickcount&reverse=true", "Genre browse", ct);

    public Task<List<RadioChannel>> GetTopStationsAsync(int limit = 20, CancellationToken ct = default) =>
        FetchAsync($"/stations/topclick/{limit}?hidebroken=true", "Top stations", ct);

    private static async Task<List<RadioChannel>> FetchAsync(string pathAndQuery, string what, CancellationToken ct)
    {
        try { return ParseStations(await GetJsonAsync(pathAndQuery, ct)); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return new List<RadioChannel>(); }
        catch (Exception ex)
        {
            Log.Warning(ex, "[RadioBrowserProvider] {What} failed", what);
            return new List<RadioChannel>();
        }
    }

    public static async Task<string?> ResolveStreamAsync(string stationName, CancellationToken ct = default)
    {
        try
        {
            var json = await GetJsonAsync(
                $"/stations/search?name={Uri.EscapeDataString(stationName)}&limit=1&order=votes&reverse=true&hidebroken=true", ct);
            return ParseStations(json).FirstOrDefault()?.StreamUrl;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return null; }
        catch (Exception ex)
        {
            Log.Warning(ex, "[RadioBrowserProvider] Resolve failed for {Name}", stationName);
            return null;
        }
    }

    private static async Task<string> GetJsonAsync(string pathAndQuery, CancellationToken ct)
    {
        Exception? last = null;
        foreach (var host in await GetServersAsync(ct))
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(8));
                return await _http.GetStringAsync($"https://{host}/json{pathAndQuery}", cts.Token);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                last = ex;
                Log.Debug("[RadioBrowser] {Host} failed: {Message}", host, ex.Message);
            }
        }
        _servers = null; // rediscover on the next call
        throw last ?? new HttpRequestException("No radio-browser server reachable");
    }

    private static async Task<string[]> GetServersAsync(CancellationToken ct)
    {
        if (_servers != null) return _servers;
        await _serverLock.WaitAsync(ct);
        try
        {
            if (_servers != null) return _servers;
            var hosts = new List<string>();
            try
            {
                var ips = await Dns.GetHostAddressesAsync("all.api.radio-browser.info", ct);
                foreach (var ip in ips)
                {
                    try
                    {
                        var entry = await Dns.GetHostEntryAsync(ip).WaitAsync(TimeSpan.FromSeconds(3), ct);
                        var host = entry.HostName.TrimEnd('.');
                        if (host.EndsWith(".radio-browser.info", StringComparison.OrdinalIgnoreCase)
                            && !hosts.Contains(host, StringComparer.OrdinalIgnoreCase))
                            hosts.Add(host);
                    }
                    catch (Exception) when (!ct.IsCancellationRequested) { }
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                Log.Debug(ex, "[RadioBrowser] DNS server discovery failed");
            }

            if (hosts.Count == 0) return new[] { FallbackHost }; // not cached, retry discovery later
            _servers = hosts.OrderBy(_ => Random.Shared.Next()).ToArray();
            Log.Information("[RadioBrowser] Using {Count} server(s): {Hosts}", _servers.Length, string.Join(", ", _servers));
            return _servers;
        }
        finally { _serverLock.Release(); }
    }

    internal static List<RadioChannel> ParseStations(string json)
    {
        var results = new List<RadioChannel>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        using var doc = JsonDocument.Parse(json);
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var name = item.TryGetProperty("name", out var n) ? n.GetString() : null;
            var url = item.TryGetProperty("url_resolved", out var u) ? u.GetString() : null;
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url)) continue;
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) continue;
            if (!seen.Add(uri.AbsoluteUri)) continue;
            results.Add(new RadioChannel(name.Trim(), url.Trim()));
        }
        return results;
    }
}