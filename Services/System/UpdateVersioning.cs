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
}