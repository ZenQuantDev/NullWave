using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Reflection;
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
    public List<UpdateAsset> Assets { get; init; } = new();
}

public record UpdateAsset(string Name, string Url);

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
        System.Reflection.Assembly.GetExecutingAssembly()
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            .Split('+')[0]
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

            var assets = new List<UpdateAsset>();
            if (root.TryGetProperty("assets", out var assetsEl))
                foreach (var a in assetsEl.EnumerateArray())
                    assets.Add(new UpdateAsset(
                        a.GetProperty("name").GetString() ?? "",
                        a.GetProperty("browser_download_url").GetString() ?? ""));

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
                PublishedAt       = published,
                Assets            = assets
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

    public async Task<bool> StageUpdateAsync(string rid)
    {
        var result = await CheckForUpdateAsync();
        if (!result.IsUpdateAvailable) return false;

        var asset = result.Assets.Find(a =>
            a.Name.Contains(rid, StringComparison.OrdinalIgnoreCase) &&
            a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

        if (asset == null || string.IsNullOrEmpty(asset.Url)) return false;

        var staging = Path.Combine(AppContext.BaseDirectory, "update");
        Directory.CreateDirectory(staging);
        var zip = Path.Combine(staging, "NullWave-update.zip");

        using var http = new HttpClient();
        await using (var src = await http.GetStreamAsync(asset.Url))
        await using (var dst = File.Create(zip))
            await src.CopyToAsync(dst);

        WriteUpdaterScripts(staging, zip);
        Log.Information("[UpdateService] Update staged at {Path}", staging);
        return true;
    }

    private static void WriteUpdaterScripts(string staging, string zip)
    {
        // Linux / macOS updater script
        var sh = Path.Combine(staging, "update.sh");
        File.WriteAllText(sh, """
            #!/bin/sh
            # usage: update.sh <pid> <zip> <dir>
            while kill -0 "$1" 2>/dev/null; do sleep 0.5; done
            tar -xf "$2" -C "$3"
            rm -f "$2"
            "$3/NullWave" &
            """);

        if (!OperatingSystem.IsWindows())
        {
            try
            {
                File.SetUnixFileMode(sh,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }
            catch { /* best effort */ }
        }

        // Windows updater script
        var ps1 = Path.Combine(staging, "update.ps1");
        File.WriteAllText(ps1, """
            param([int]$Pid2, [string]$Zip, [string]$Dir)
            Wait-Process -Id $Pid2 -ErrorAction SilentlyContinue
            Expand-Archive -LiteralPath $Zip -DestinationPath $Dir -Force
            Remove-Item $Zip -Force
            Start-Process (Join-Path $Dir "NullWave.exe")
            """);
    }

    public void LaunchUpdaterAndExit()
    {
        var staging = Path.Combine(AppContext.BaseDirectory, "update");
        var zip = Path.Combine(staging, "NullWave-update.zip");
        var dir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var pid = Environment.ProcessId;

        var psi = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("powershell",
                $"-ExecutionPolicy Bypass -File \"{Path.Combine(staging, "update.ps1")}\" {pid} \"{zip}\" \"{dir}\"")
            : new ProcessStartInfo("/bin/sh",
                $"\"{Path.Combine(staging, "update.sh")}\" {pid} \"{zip}\" \"{dir}\"");

        psi.UseShellExecute = false;
        Process.Start(psi);
        Environment.Exit(0);
    }

    private static bool IsNewerVersion(string latest, string current)
    {
        if (!Version.TryParse(latest, out var l)) return false;
        if (!Version.TryParse(current, out var c)) return false;
        return l > c;
    }
}