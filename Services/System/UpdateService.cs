using System;
using System.Reflection;
using System.Threading.Tasks;
using Serilog;
using Velopack;
using Velopack.Sources;

namespace NullWave.Services;

public class UpdateCheckResult
{
    public bool IsUpdateAvailable { get; init; }
    public bool Failed { get; init; }
    public bool NotInstalled { get; init; }
    public string CurrentVersion  { get; init; } = string.Empty;
    public string LatestVersion   { get; init; } = string.Empty;
    public string ReleaseUrl      { get; init; } = string.Empty;
    public string ReleaseNotes    { get; init; } = string.Empty;
}

public class UpdateService
{
    private const string RepoUrl = "https://github.com/ZenQuantDev/NullWave";
    
    private readonly UpdateManager _updateManager;
    private UpdateInfo? _pendingUpdate;
    private readonly string _displayVersion;

    public UpdateService()
    {
        // Get the real build version from assembly metadata (e.g., "0.6.5-beta.1+commit")
        var infoVer = typeof(UpdateService).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        _displayVersion = infoVer.Split('+')[0]; // Strip commit hash if present
        
        // Detect if this is a pre-release build (contains '-' like 0.6.5-beta.1)
        var isPreRelease = _displayVersion.Contains('-');
        
        var source = new GithubSource(RepoUrl, accessToken: null, prerelease: isPreRelease);
        _updateManager = new UpdateManager(source);
        
        Log.Information("[UpdateService] Initialized. IsInstalled={IsInstalled}, Version={Version}, PreRelease={PreRelease}", 
            _updateManager.IsInstalled, _displayVersion, isPreRelease);
    }

    public bool IsInstalled => _updateManager.IsInstalled;
    public string CurrentVersion => _updateManager.CurrentVersion?.ToString() ?? _displayVersion;

    public async Task<UpdateCheckResult> CheckForUpdateAsync()
    {
        if (!_updateManager.IsInstalled)
        {
            Log.Information("[UpdateService] Skipping check: App is not installed via Velopack (dev/portable build).");
            return new UpdateCheckResult
            {
                NotInstalled = true,
                IsUpdateAvailable = false,
                CurrentVersion = _displayVersion,
                LatestVersion = _displayVersion,
                ReleaseUrl = $"{RepoUrl}/releases",
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
                    ReleaseUrl = $"{RepoUrl}/releases"
                };
            }

            var versionString = _pendingUpdate.TargetFullRelease.Version.ToString();
            Log.Information("[UpdateService] Update available: v{Version}", versionString);

            return new UpdateCheckResult
            {
                IsUpdateAvailable = true,
                CurrentVersion = CurrentVersion,
                LatestVersion = versionString,
                ReleaseUrl = $"{RepoUrl}/releases/tag/v{versionString}",
                ReleaseNotes = _pendingUpdate.TargetFullRelease.NotesMarkdown ?? "See GitHub for release notes."
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[UpdateService] Update check failed");
            return new UpdateCheckResult
            {
                Failed = true,
                IsUpdateAvailable = false,
                CurrentVersion = CurrentVersion,
                LatestVersion = "unknown"
            };
        }
    }

    public async Task<bool> StageUpdateAsync(Action<int>? progress = null)
    {
        if (!_updateManager.IsInstalled) return false;

        try
        {
            if (_pendingUpdate == null)
                _pendingUpdate = await _updateManager.CheckForUpdatesAsync();
                
            if (_pendingUpdate == null) return false;

            Log.Information("[UpdateService] Downloading update v{Version}...", _pendingUpdate.TargetFullRelease.Version);
            await _updateManager.DownloadUpdatesAsync(_pendingUpdate, progress);
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

        // FIX: No sync-over-async. If we don't have a pending update, bail out.
        // The UI should only show this button when UpdateStaged is true, which means
        // StageUpdateAsync already succeeded and _pendingUpdate is set.
        if (_pendingUpdate == null)
        {
            Log.Warning("[UpdateService] ApplyUpdatesAndRestart called but no update is staged.");
            return;
        }
        
        try
        {
            Log.Information("[UpdateService] Applying update v{Version} and restarting...", _pendingUpdate.TargetFullRelease.Version);
            _updateManager.ApplyUpdatesAndRestart(_pendingUpdate);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[UpdateService] Failed to apply update and restart");
        }
    }
}