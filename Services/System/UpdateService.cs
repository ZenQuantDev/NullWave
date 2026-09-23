using System;
using System.Threading.Tasks;
using Serilog;
using Velopack;
using Velopack.Sources;

namespace NullWave.Services;

public class UpdateCheckResult
{
    public bool IsUpdateAvailable { get; init; }
    public string CurrentVersion  { get; init; } = string.Empty;
    public string LatestVersion   { get; init; } = string.Empty;
    public string ReleaseUrl      { get; init; } = string.Empty;
    public string ReleaseNotes    { get; init; } = string.Empty;
}

public class UpdateService
{
    private readonly UpdateManager _updateManager;
    private UpdateInfo? _pendingUpdate;

    public UpdateService()
    {
        // Point to your GitHub repository
        var source = new GithubSource("https://github.com/ZenQuantDev/NullWave", accessToken: null, prerelease: false);
        _updateManager = new UpdateManager(source);
    }

    /// <summary>
    /// True if the app was installed via Velopack Setup/Installer. 
    /// False if running from source/IDE (dotnet run), which prevents NotInstalledException.
    /// </summary>
    public bool IsInstalled => _updateManager.IsInstalled;

    public string CurrentVersion => _updateManager.CurrentVersion?.ToString() ?? "0.0.0";

    public async Task<UpdateCheckResult> CheckForUpdateAsync()
    {
        // Guard against dev builds throwing NotInstalledException
        if (!_updateManager.IsInstalled)
        {
            return new UpdateCheckResult
            {
                IsUpdateAvailable = false,
                CurrentVersion = "dev build",
                LatestVersion = "dev build",
                ReleaseUrl = "https://github.com/ZenQuantDev/NullWave/releases",
                ReleaseNotes = "Updates are not available in development builds."
            };
        }

        try
        {
            _pendingUpdate = await _updateManager.CheckForUpdatesAsync();
            if (_pendingUpdate == null)
            {
                return new UpdateCheckResult
                {
                    IsUpdateAvailable = false,
                    CurrentVersion = CurrentVersion,
                    LatestVersion = CurrentVersion,
                    ReleaseUrl = "https://github.com/ZenQuantDev/NullWave/releases"
                };
            }

            var versionString = _pendingUpdate.TargetFullRelease.Version.ToString();
            return new UpdateCheckResult
            {
                IsUpdateAvailable = true,
                CurrentVersion = CurrentVersion,
                LatestVersion = versionString,
                ReleaseUrl = $"https://github.com/ZenQuantDev/NullWave/releases/tag/v{versionString}",
                ReleaseNotes = "See GitHub for release notes."
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[UpdateService] Update check failed");
            return new UpdateCheckResult
            {
                IsUpdateAvailable = false,
                CurrentVersion = CurrentVersion,
                LatestVersion = "unknown"
            };
        }
    }

    public async Task<bool> StageUpdateAsync()
    {
        if (!_updateManager.IsInstalled) return false;

        try
        {
            if (_pendingUpdate == null)
                _pendingUpdate = await _updateManager.CheckForUpdatesAsync();
                
            if (_pendingUpdate == null) return false;

            // Velopack downloads, verifies hashes, and stages the update automatically
            await _updateManager.DownloadUpdatesAsync(_pendingUpdate);
            Log.Information("[UpdateService] Update staged successfully via Velopack.");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[UpdateService] Failed to download/stage update");
            return false;
        }
    }

    public void ApplyUpdatesAndRestart()
    {
        if (!_updateManager.IsInstalled)
        {
            Log.Warning("[UpdateService] Cannot apply updates: app is not installed via Velopack.");
            return;
        }

        try
        {
            if (_pendingUpdate == null)
            {
                _pendingUpdate = _updateManager.CheckForUpdatesAsync().GetAwaiter().GetResult();
            }
            
            if (_pendingUpdate != null)
            {
                Log.Information("[UpdateService] Applying update and restarting...");
                // Velopack handles the graceful shutdown, file swap, and restart securely
                _updateManager.ApplyUpdatesAndRestart(_pendingUpdate.TargetFullRelease);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[UpdateService] Failed to apply update and restart");
        }
    }
}