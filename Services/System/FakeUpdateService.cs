using System;
using System.Threading.Tasks;
using Serilog;

namespace NullWave.Services;

/// <summary>
/// Fake update service for dev-mode testing. Reads NULLWAVE_FAKE_UPDATE env var:
/// - "available": simulates an update available
/// - "fail": simulates a download failure
/// - "checkfail": simulates a check failure
/// - "staged": simulates a staged update ready to install
/// - unset/other: simulates "up to date"
/// </summary>
public class FakeUpdateService : IUpdateService
{
    private readonly string _mode;
    private bool _staged;

    public FakeUpdateService()
    {
        _mode = Environment.GetEnvironmentVariable("NULLWAVE_FAKE_UPDATE") ?? "uptodate";
        Log.Information("[FakeUpdateService] Initialized in {Mode} mode", _mode);
    }

    public bool IsInstalled => true;
    public bool HasStagedUpdate => _staged || _mode == "staged";
    public string? StagedVersion => HasStagedUpdate ? "99.99.99" : null;
    public string CurrentVersion => "0.0.0-dev";

    public Task<UpdateCheckResult> CheckForUpdateAsync()
    {
        return Task.FromResult(_mode switch
        {
            "available" or "staged" or "fail" => new UpdateCheckResult
            {
                IsUpdateAvailable = true,
                CurrentVersion = CurrentVersion,
                LatestVersion = "99.99.99",
                ReleaseUrl = "https://github.com/ZenQuantDev/NullWave/releases"
            },
            "checkfail" => new UpdateCheckResult
            {
                Failed = true,
                CurrentVersion = CurrentVersion,
                LatestVersion = "unknown"
            },
            _ => new UpdateCheckResult
            {
                IsUpdateAvailable = false,
                CurrentVersion = CurrentVersion,
                LatestVersion = CurrentVersion
            }
        });
    }

    public async Task<StageResult> StageUpdateAsync(Action<int>? progress = null)
    {
        if (_mode == "fail")
            return new(StageOutcome.Failed, "Simulated download failure");

        for (int i = 0; i <= 100; i += 10)
        {
            await Task.Delay(100);
            progress?.Invoke(i);
        }

        _staged = true;
        return new(StageOutcome.Staged);
    }

    public void ApplyUpdatesAndRestart()
    {
        Log.Information("[FakeUpdateService] Would apply staged update and restart");
    }
}