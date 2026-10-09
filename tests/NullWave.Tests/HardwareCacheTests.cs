using NullWave.Services.SmartSorting;
using NullWave.Tests.Support;

namespace NullWave.Tests.SmartSorting;

[Collection("Hardware")]
public class HardwareCacheTests : IDisposable
{
    public HardwareCacheTests() => HardwareDetector.ResetCacheForTests();
    public void Dispose() => HardwareDetector.ResetCacheForTests();

    [Fact]
    public async Task Concurrent_callers_share_one_detection()
    {
        var calls = 0;
        var gate = new TaskCompletionSource<HardwareInfo>();
        HardwareDetector.DetectFactory = _ =>
        {
            Interlocked.Increment(ref calls);
            return gate.Task;
        };

        var tasks = new Task<HardwareInfo>[10];
        Parallel.For(0, tasks.Length, i => tasks[i] = HardwareDetector.GetCachedInfoAsync());

        gate.SetResult(new HardwareInfo { CpuCores = 4 });
        var results = await Task.WhenAll(tasks);

        Assert.Equal(1, calls);
        Assert.All(tasks, task => Assert.Same(tasks[0], task));
        Assert.All(results, result => Assert.Equal(4, result.CpuCores));
    }

    [Fact]
    public async Task A_completed_detection_is_cached()
    {
        var calls = 0;
        HardwareDetector.DetectFactory = _ =>
        {
            calls++;
            return Task.FromResult(new HardwareInfo { CpuCores = 8 });
        };

        await HardwareDetector.GetCachedInfoAsync();
        await HardwareDetector.GetCachedInfoAsync();

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task A_faulted_detection_is_not_cached()
    {
        var calls = 0;
        HardwareDetector.DetectFactory = _ =>
        {
            calls++;
            return calls == 1
                ? Task.FromException<HardwareInfo>(new InvalidOperationException("boom"))
                : Task.FromResult(new HardwareInfo { CpuCores = 4 });
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => HardwareDetector.GetCachedInfoAsync());
        var second = await HardwareDetector.GetCachedInfoAsync();

        Assert.Equal(2, calls);
        Assert.Equal(4, second.CpuCores);
    }

    [Fact]
    public async Task CachedOrNull_is_null_until_detection_completes()
    {
        var gate = new TaskCompletionSource<HardwareInfo>();
        HardwareDetector.DetectFactory = _ => gate.Task;

        Assert.Null(HardwareDetector.CachedOrNull());
        var pending = HardwareDetector.GetCachedInfoAsync();
        Assert.Null(HardwareDetector.CachedOrNull());

        gate.SetResult(new HardwareInfo { CpuCores = 6 });
        await pending;

        Assert.Equal(6, HardwareDetector.CachedOrNull()?.CpuCores);
    }

    [Fact]
    public async Task RefreshAsync_runs_detection_again()
    {
        var calls = 0;
        HardwareDetector.DetectFactory = _ =>
        {
            calls++;
            return Task.FromResult(new HardwareInfo { CpuCores = calls });
        };

        var first = await HardwareDetector.GetCachedInfoAsync();
        var refreshed = await HardwareDetector.RefreshAsync();

        Assert.Equal(2, calls);
        Assert.Equal(1, first.CpuCores);
        Assert.Equal(2, refreshed.CpuCores);
    }

    [Fact]
    public void Only_the_detector_itself_creates_detectors()
    {
        var offenders = RepoFiles.SourceFiles()
            .Where(path => Path.GetFileName(path) is not ("HardwareDetector.cs" or "HardwareInfo.cs"))
            .Where(path => File.ReadAllText(path).Contains("new HardwareDetector(", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(offenders.Count == 0,
            "These files create their own HardwareDetector (use GetCachedInfoAsync/RefreshAsync): "
            + string.Join(", ", offenders));
    }
}