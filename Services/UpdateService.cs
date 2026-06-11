using System;
using System.Net.Http;
using System.Text.Json;
using System.Reflection;
using System.Threading.Tasks;
using Serilog;

namespace NullWave.Services;

public class UpdateCheckResult
{
    public bool IsUpdateAvailable { get; init; }
    public string CurrentVersion  { get; init; } = string.Empty;
    public string LatestVersion   { get; init; } = string.Empty;
    public string ReleaseUrl      { get; init; } = string.Empty;
    public string ReleaseNotes    { get; init; } = string.Empty;
    public DateTime? PublishedAt  { get; init; }
}

public class UpdateService
{
    private const string ApiUrl =
        "https://api.github.com/repos/Alexsandrgardaphadze/NullWave/releases/latest";

    private readonly HttpClient _http;

    public UpdateService()
    {
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Add("User-Agent", "NullWave-UpdateChecker");
    }

    public string CurrentVersion =>
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            .Split('+')[0]   // strip git hash suffix
            ?? "unknown";

    public async Task<UpdateCheckResult> CheckForUpdateAsync()
    {
        try
        {
            var response = await _http.GetAsync(ApiUrl);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                Log.Information("[UpdateService] No releases found on GitHub yet");
                return new UpdateCheckResult
                {
                    IsUpdateAvailable = false,
                    CurrentVersion    = CurrentVersion,
                    LatestVersion     = "No releases yet"
                };
            }
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var latest     = root.GetProperty("tag_name").GetString()?.TrimStart('v') ?? "0.0.0";
            var releaseUrl = root.GetProperty("html_url").GetString() ?? string.Empty;
            var body       = root.GetProperty("body").GetString() ?? string.Empty;
            DateTime? published = null;

            if (root.TryGetProperty("published_at", out var pub) &&
                DateTime.TryParse(pub.GetString(), out var dt))
                published = dt;

            var current = CurrentVersion.Split('+')[0];
            var isNewer = IsNewerVersion(latest, current);

            Log.Information("[UpdateService] Current: {Current} | Latest: {Latest} | Update: {Update}",
                current, latest, isNewer);

            return new UpdateCheckResult
            {
                IsUpdateAvailable = isNewer,
                CurrentVersion    = current,
                LatestVersion     = latest,
                ReleaseUrl        = releaseUrl,
                ReleaseNotes      = body.Length > 500 ? body[..500] + "..." : body,
                PublishedAt       = published
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[UpdateService] Update check failed");
            return new UpdateCheckResult
            {
                IsUpdateAvailable = false,
                CurrentVersion    = CurrentVersion,
                LatestVersion     = "unknown"
            };
        }
    }

    private static bool IsNewerVersion(string latest, string current)
    {
        if (!Version.TryParse(latest,  out var l)) return false;
        if (!Version.TryParse(current, out var c)) return false;
        return l > c;
    }
}