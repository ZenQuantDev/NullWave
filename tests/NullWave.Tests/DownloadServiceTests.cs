using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NullWave.Helpers;
using NullWave.Services;
using Xunit;

namespace NullWave.Tests;

[Collection("Database")] 
public class DownloadServiceTests : IDisposable
{
    private readonly string _downloadDir;
    private readonly DownloadService _service;

    public DownloadServiceTests()
    {
        // AppContext.BaseDirectory is tests/NullWave.Tests/bin/Debug/net8.0/
        // We go up 4 levels to reach the 'tests' folder.
        var baseDir = new DirectoryInfo(AppContext.BaseDirectory);
        var testsDir = baseDir.Parent?.Parent?.Parent?.Parent;
        
        if (testsDir == null)
            throw new DirectoryNotFoundException("Could not locate the 'tests' directory from AppContext.BaseDirectory.");

        // FakeYtDlp is a sibling folder to NullWave.Tests
        var fakeExe = Path.Combine(testsDir.FullName, "FakeYtDlp", "bin", "Debug", "net8.0", 
            OperatingSystem.IsWindows() ? "FakeYtDlp.exe" : "FakeYtDlp");
            
        if (!File.Exists(fakeExe))
            throw new FileNotFoundException($"FakeYtDlp not found at {fakeExe}. Ensure the ProjectReference is correct and the solution is built.");

        // Point PlatformHelper directly to the compiled executable
        Environment.SetEnvironmentVariable("NULLWAVE_TOOL_YT_DLP", fakeExe);
        
        _downloadDir = NullWavePaths.DownloadsDir;
        Directory.CreateDirectory(_downloadDir);
        Environment.SetEnvironmentVariable("FAKE_YTDLP_OUTDIR", _downloadDir);

        var prefs = new PreferencesService();
        _service = new DownloadService(null!, prefs, null!);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("NULLWAVE_TOOL_YT_DLP", null);
        Environment.SetEnvironmentVariable("FAKE_YTDLP_MODE", null);
        Environment.SetEnvironmentVariable("FAKE_YTDLP_OUTDIR", null);
    }

    [Fact]
    public async Task Successful_download_fires_Completed_event()
    {
        Environment.SetEnvironmentVariable("FAKE_YTDLP_MODE", "success");
        
        string? completedPath = null;
        _service.DownloadCompleted += (id, path, isInteractive) => completedPath = path;

        await _service.DownloadAsync("track-1", "https://youtube.com/watch?v=abc", isInteractive: false);

        Assert.NotNull(completedPath);
        Assert.True(File.Exists(completedPath));
    }

    [Fact]
    public async Task Duplicate_URL_fires_Failed_event_immediately_and_does_not_hang()
    {
        Environment.SetEnvironmentVariable("FAKE_YTDLP_MODE", "hang"); 
        
        var cts1 = new CancellationTokenSource();
        var task1 = _service.DownloadAsync("track-1", "https://youtube.com/watch?v=DUPLICATE", isInteractive: false, ct: cts1.Token);

        await Task.Delay(500); 

        string? failedReason = null;
        _service.DownloadFailed += (id, reason, isInteractive) => failedReason = reason;

        var task2 = _service.DownloadAsync("track-2", "https://youtube.com/watch?v=DUPLICATE", isInteractive: false);
        
        await Task.WhenAny(task2, Task.Delay(2000));

        Assert.Equal("Already downloading", failedReason);
        
        cts1.Cancel();
        await Task.WhenAny(task1, Task.Delay(1000));
    }

    [Fact]
    public async Task Cancelled_while_queued_removes_URL_from_active_downloads()
    {
        Environment.SetEnvironmentVariable("FAKE_YTDLP_MODE", "hang");
        
        _service.UpdateConcurrencyLimit(1);

        var cts1 = new CancellationTokenSource();
        var task1 = _service.DownloadAsync("track-1", "https://youtube.com/watch?v=FIRST", isInteractive: false, ct: cts1.Token);
        await Task.Delay(500); 

        var cts2 = new CancellationTokenSource();
        var task2 = _service.DownloadAsync("track-2", "https://youtube.com/watch?v=QUEUED", isInteractive: false, ct: cts2.Token);
        await Task.Delay(200); 

        cts2.Cancel();
        await Task.WhenAny(task2, Task.Delay(1000));

        Environment.SetEnvironmentVariable("FAKE_YTDLP_MODE", "success");
        
        bool completed = false;
        _service.DownloadCompleted += (id, path, isInteractive) => { if (id == "track-3") completed = true; };
        
        _service.UpdateConcurrencyLimit(2); 
        
        await _service.DownloadAsync("track-3", "https://youtube.com/watch?v=QUEUED", isInteractive: false);

        Assert.True(completed, "The URL was permanently stuck in _activeDownloads after being cancelled while queued.");

        cts1.Cancel();
    }
}