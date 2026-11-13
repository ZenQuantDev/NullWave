using System;

namespace NullWave.Services;

public static class UpdateVersioning
{
    public static string Display(string? info) =>
        string.IsNullOrWhiteSpace(info) ? "0.0.0" : info.Split('+')[0];

    public static bool IsPreRelease(string? info) => Display(info).Contains('-');

    public static string StatusText(UpdateCheckResult r) => r switch
    {
        { NotInstalled: true }      => "Dev build: updates need an installed copy",
        { Failed: true }            => "Couldn't check for updates (offline or rate-limited)",
        { IsUpdateAvailable: true } => $"Update available: v{r.LatestVersion}",
        _                           => $"You are up to date (v{r.CurrentVersion})"
    };

    public static string StatusText(UpdateCheckResult r, Func<string, string> loc)
    {
        static string F(string format, string value) => string.Format(System.Globalization.CultureInfo.InvariantCulture, format, value);
        return r switch
        {
            { NotInstalled: true }      => loc("Settings_Updates_Status_Dev"),
            { Failed: true }            => loc("Settings_Updates_Status_CheckFailed"),
            { IsUpdateAvailable: true } => F(loc("Settings_Updates_Status_Available"), r.LatestVersion),
            _                           => F(loc("Settings_Updates_Status_UpToDate"), r.CurrentVersion)
        };
    }
}