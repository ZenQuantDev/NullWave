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
        var source = new GithubSource("https://github.com/ZenQuantDev/NullWave", accessToken: null, prerelease: false);
        _updateManager = new UpdateManager(source);
        
        // Log initialization state
        Log.Information("[UpdateService] Initialized. IsInstalled={IsInstalled}, Version={Version}", 
            _updateManager.IsInstalled, CurrentVersion);
    }

    public bool IsInstalled => _updateManager.IsInstalled;
    public string CurrentVersion => _updateManager.CurrentVersion?.ToString() ?? "0.0.0";

    public async Task<UpdateCheckResult> CheckForUpdateAsync()
    {
        if (!_updateManager.IsInstalled)
        {
            Log.Information("[UpdateService] Skipping check: App is not installed via Velopack (dev/portable build).");
            return new UpdateCheckResult
            {
                IsUpdateAvailable = false,
                CurrentVersion = "dev build",
                LatestVersion = "dev build",
                ReleaseUrl = "https://github.com/ZenQuantDev/NullWave/releases",
                ReleaseNotes = "Updates are only available for installed builds."
            };
        }

        try
        {
            Log.Information("[UpdateService] Checking for updates (Current: v{Version})...", CurrentVersion);
            _pendingUpdate = await _updateManager.CheckForUpdatesAsync();

            if (_pendingUpdate == null)
            {
                Log.Information("[UpdateService] No update found. v{Version} is up to date.", CurrentVersion);
                return new UpdateCheckResult
                {
                    IsUpdateAvailable = false,
                    CurrentVersion = CurrentVersion,
                    LatestVersion = CurrentVersion,
                    ReleaseUrl = "https://github.com/ZenQuantDev/NullWave/releases"
                };
            }

            var versionString = _pendingUpdate.TargetFullRelease.Version.ToString();
            Log.Information("[UpdateService] Update available: v{Version}", versionString);

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

            Log.Information("[UpdateService] Downloading update v{Version}...", _pendingUpdate.TargetFullRelease.Version);
            await _updateManager.DownloadUpdatesAsync(_pendingUpdate);
            Log.Information("[UpdateService] Update staged successfully.");
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
                Log.Information("[UpdateService] Applying update v{Version} and restarting...", _pendingUpdate.TargetFullRelease.Version);
                _updateManager.ApplyUpdatesAndRestart(_pendingUpdate.TargetFullRelease);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[UpdateService] Failed to apply update and restart");
        }
    }
}