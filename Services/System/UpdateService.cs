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

public enum StageOutcome { Staged, NoUpdate, NotInstalled, Failed }
public sealed record StageResult(StageOutcome Outcome, string? Error = null);

public interface IUpdateService
{
    bool IsInstalled { get; }
    bool HasStagedUpdate { get; }
    string? StagedVersion { get; }
    string CurrentVersion { get; }
    Task<UpdateCheckResult> CheckForUpdateAsync();
    Task<StageResult> StageUpdateAsync(Action<int>? progress = null);
    void ApplyUpdatesAndRestart();
}

public class UpdateService : IUpdateService
{
    private const string RepoUrl = "https://github.com/ZenQuantDev/NullWave";
    
    private readonly UpdateManager _updateManager;
    private UpdateInfo? _pendingUpdate;
    private readonly string _displayVersion;
    public bool HasStagedUpdate => _updateManager.IsInstalled && _updateManager.UpdatePendingRestart != null;
    public string? StagedVersion => _updateManager.UpdatePendingRestart?.Version?.ToString();

    public UpdateService()
    {
        var infoVer = typeof(UpdateService).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
            
        _displayVersion = UpdateVersioning.Display(infoVer);
        var isPreRelease = UpdateVersioning.IsPreRelease(infoVer);
        
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

    public async Task<StageResult> StageUpdateAsync(Action<int>? progress = null)
    {
        if (!_updateManager.IsInstalled) return new(StageOutcome.NotInstalled);
        try
        {
            _pendingUpdate ??= await _updateManager.CheckForUpdatesAsync();
            if (_pendingUpdate == null) return new(StageOutcome.NoUpdate);

            Log.Information("[UpdateService] Downloading update v{Version}...", _pendingUpdate.TargetFullRelease.Version);
            await _updateManager.DownloadUpdatesAsync(_pendingUpdate, progress);
            Log.Information("[UpdateService] Update staged successfully.");
            return new(StageOutcome.Staged);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[UpdateService] Failed to download/stage update");
            return new(StageOutcome.Failed, ex.Message);
        }
    }

    public void ApplyUpdatesAndRestart()
    {
        if (!_updateManager.IsInstalled)
        {
            Log.Warning("[UpdateService] Cannot apply updates: app is not installed via Velopack.");
            return;
        }

        var asset = _pendingUpdate?.TargetFullRelease ?? _updateManager.UpdatePendingRestart;
        if (asset == null)
        {
            Log.Warning("[UpdateService] ApplyUpdatesAndRestart called but no update is staged.");
            return;
        }
        
        try
        {
            Log.Information("[UpdateService] Applying update v{Version} and restarting...", asset.Version);
            _updateManager.ApplyUpdatesAndRestart(asset);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[UpdateService] Failed to apply update and restart");
        }
    }
}