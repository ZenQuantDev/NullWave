using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NullWave.Helpers;
using NullWave.Models;
using NullWave.Services;
using Xunit;

namespace NullWave.Tests;

[Collection("Database")] 
public class DownloadServiceTests : IDisposable
{
    private readonly string _downloadDir;
    private readonly DownloadService _service;
    private readonly PreferencesService _prefs = null!;

    public DownloadServiceTests()
    {
        var baseDir = new DirectoryInfo(AppContext.BaseDirectory);
        var testsDir = baseDir.Parent?.Parent?.Parent?.Parent;
        
        if (testsDir == null)
            throw new DirectoryNotFoundException("Could not locate the 'tests' directory from AppContext.BaseDirectory.");

        var fakeExe = Path.Combine(testsDir.FullName, "FakeYtDlp", "bin", "Debug", "net10.0", 
            OperatingSystem.IsWindows() ? "FakeYtDlp.exe" : "FakeYtDlp");
            
        if (!File.Exists(fakeExe))
            throw new FileNotFoundException($"FakeYtDlp not found at {fakeExe}. Ensure the ProjectReference is correct and the solution is built.");

        Environment.SetEnvironmentVariable("NULLWAVE_TOOL_YT_DLP", fakeExe);
        
        _downloadDir = NullWavePaths.DownloadsDir;
        Directory.CreateDirectory(_downloadDir);
        Environment.SetEnvironmentVariable("FAKE_YTDLP_OUTDIR", _downloadDir);

        _prefs = new PreferencesService();
        var consent = new DownloaderConsent
        {
            AcceptedVersion = DownloaderConsent.CurrentVersion,
            AcceptedAtUtc = DateTime.UtcNow
        };
        _prefs.Update(p => p.DownloaderConsent = consent);

        _service = new DownloadService(null!, _prefs, null!);
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
    public async Task Duplicate_URL_returns_silently_and_does_not_hang()
    {
        Environment.SetEnvironmentVariable("FAKE_YTDLP_MODE", "hang"); 
        
        var cts1 = new CancellationTokenSource();
        var task1 = _service.DownloadAsync("track-1", "https://youtube.com/watch?v=DUPLICATE", isInteractive: false, ct: cts1.Token);

        await Task.Delay(500); 

        string? failedReason = null;
        _service.DownloadFailed += (id, reason, isInteractive) => failedReason = reason;

        var task2 = _service.DownloadAsync("track-2", "https://youtube.com/watch?v=DUPLICATE", isInteractive: false);
        
        await Task.WhenAny(task2, Task.Delay(2000));

        Assert.True(task2.IsCompleted, "Duplicate request should return immediately, not hang.");
        Assert.Null(failedReason);
        
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

    [Fact]
    public async Task Job_receives_title_artist_and_trackId_when_provided()
    {
        Environment.SetEnvironmentVariable("FAKE_YTDLP_MODE", "success");

        await _service.DownloadAsync("track-title-1", "https://youtube.com/watch?v=TITLEJOB",
            isInteractive: false, title: "My Song", artist: "My Artist");

        var job = _service.ActiveJobs.FirstOrDefault(j => j.Url == "https://youtube.com/watch?v=TITLEJOB");
        Assert.NotNull(job);
        Assert.Equal("My Song", job!.Title);
        Assert.Equal("My Artist", job.Artist);
        Assert.Equal("track-title-1", job.TrackId);
    }

    [Fact]
    public void UpdateJobMetadata_updates_the_matching_active_job()
    {
        Environment.SetEnvironmentVariable("FAKE_YTDLP_MODE", "hang");
        var cts = new CancellationTokenSource();

        _ = _service.DownloadAsync("track-meta-1", "https://youtube.com/watch?v=METAJOB",
            isInteractive: false, ct: cts.Token);
        Thread.Sleep(300); 

        _service.UpdateJobMetadata("track-meta-1", "Real Title", "Real Artist");

        var job = _service.ActiveJobs.FirstOrDefault(j => j.TrackId == "track-meta-1");
        Assert.NotNull(job);
        Assert.Equal("Real Title", job!.Title);
        Assert.Equal("Real Artist", job.Artist);

        cts.Cancel();
    }
    
    [Theory]
    [InlineData(false, "mp3",  true,  "bestaudio[ext=m4a]/bestaudio/best")]
    [InlineData(true,  "best", true,  "bestaudio[ext=m4a]/bestaudio/best")]
    [InlineData(true,  "mp3",  true,  "bestaudio/best")]
    [InlineData(false, "mp3",  false, "bestaudio/best")]
    public void Format_selector_forces_m4a_only_on_windows_native_containers(
        bool useFfmpeg, string format, bool isWindows, string expected)
        => Assert.Equal(expected, DownloadService.ChooseFormatSelector(useFfmpeg, format, isWindows));

        [Fact]
    public async Task Changing_limit_mid_download_does_not_throw_or_lose_the_track()
    {
        Environment.SetEnvironmentVariable("FAKE_YTDLP_MODE", "hang");
        string? failure = null;
        _service.DownloadFailed += (_, reason, _) => failure = reason;
        
        var cts = new CancellationTokenSource();
        var t1 = _service.DownloadAsync("a", "https://youtube.com/watch?v=LIM1", isInteractive: false, ct: cts.Token);
        var t2 = _service.DownloadAsync("b", "https://youtube.com/watch?v=LIM2", isInteractive: false, ct: cts.Token);
        await Task.Delay(500);

        _service.UpdateConcurrencyLimit(3);
        _service.UpdateConcurrencyLimit(1);
        cts.Cancel();
        await Task.WhenAll(t1, t2);

        Assert.DoesNotContain("disposed", failure ?? "", StringComparison.OrdinalIgnoreCase);

        Environment.SetEnvironmentVariable("FAKE_YTDLP_MODE", "success");
        bool done = false;
        _service.DownloadCompleted += (_, _, _) => done = true;
        await _service.DownloadAsync("c", "https://youtube.com/watch?v=LIM3", isInteractive: false);
        Assert.True(done);
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?list=PLx&v=abc", "https://www.youtube.com/watch?v=abc")]
    [InlineData("https://www.youtube.com/watch?v=abc&list=RDabc&index=3&start_radio=1", "https://www.youtube.com/watch?v=abc")]
    [InlineData("https://www.youtube.com/watch?v=abc&t=30s&list=PLx", "https://www.youtube.com/watch?v=abc&t=30s")]
    [InlineData("https://youtu.be/abc?list=PLx", "https://youtu.be/abc")]
    [InlineData("https://www.youtube.com/watch?v=abc", "https://www.youtube.com/watch?v=abc")]
    public void StripListParam_keeps_the_video_and_drops_the_list(string input, string expected)
        => Assert.Equal(expected, DownloadService.StripListParam(input));

    [Fact]
    public async Task Limit_changes_settle_to_the_requested_number_of_permits()
    {
        foreach (var step in new[] { 5, 1, 5, 3, 1, 3 })
        {
            _service.UpdateConcurrencyLimit(step);
            await Task.Delay(150);
            Assert.Equal(step, _service.AvailablePermits);
        }
    }

    [Fact]
    public async Task Rapid_limit_flips_do_not_leave_the_gate_stuck()
    {
        _service.UpdateConcurrencyLimit(5); _service.UpdateConcurrencyLimit(1); _service.UpdateConcurrencyLimit(5);
        await Task.Delay(300);
        Assert.Equal(5, _service.AvailablePermits);
    }

    [Fact]
    public async Task A_second_interactive_download_does_not_cancel_the_first()
    {
        Environment.SetEnvironmentVariable("FAKE_YTDLP_MODE", "hang");
        string? failed = null;
        _service.DownloadFailed += (id, reason, _) => { if (id == "first") failed = reason; };
        var cts = new CancellationTokenSource();
        var t1 = _service.DownloadAsync("first",  "https://youtube.com/watch?v=INT1", ct: cts.Token);   // interactive by default
        await Task.Delay(500);
        var t2 = _service.DownloadAsync("second", "https://youtube.com/watch?v=INT2", ct: cts.Token);
        await Task.Delay(500);
        Assert.Null(failed);
        cts.Cancel();
        await Task.WhenAll(t1, t2);
    }

    [Theory]
    [InlineData("https://artist.bandcamp.com/track/x", true, false)]
    [InlineData("https://www.youtube.com/watch?v=ARGS1", false, true)]
    public async Task Arguments_depend_on_the_source(string url, bool playlistItems, bool parseMetadata)
    {
        var argsFile = Path.Combine(Path.GetTempPath(), $"fy-{Guid.NewGuid():N}.txt");
        Environment.SetEnvironmentVariable("FAKE_YTDLP_ARGSFILE", argsFile);
        Environment.SetEnvironmentVariable("FAKE_YTDLP_MODE", "success");
        try
        {
            await _service.DownloadAsync("t", url, isInteractive: false);
            var args = File.ReadAllLines(argsFile);
            Assert.Equal(playlistItems, args.Contains("--playlist-items"));
            Assert.Equal(parseMetadata, args.Contains("--parse-metadata"));
            Assert.Contains("--encoding", args);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FAKE_YTDLP_ARGSFILE", null);
            try { File.Delete(argsFile); } catch { }
        }
    }
}