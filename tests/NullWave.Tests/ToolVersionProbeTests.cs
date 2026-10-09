using NullWave.Services;

namespace NullWave.Tests.Tools;

/// <summary>
/// The probe keeps a static cache and a static resolver seam, so these tests share one xUnit
/// collection and reset both before and after each test. No real process is ever started.
/// </summary>
[Collection("ToolProbe")]
public class ToolVersionProbeTests : IDisposable
{
    public ToolVersionProbeTests() => ToolVersionProbe.ResetForTests();
    public void Dispose() => ToolVersionProbe.ResetForTests();

    [Fact]
    public async Task A_found_version_is_cached()
    {
        var calls = 0;
        ToolVersionProbe.Resolver = (_, _) =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult<string?>("2026.08.19");
        };

        var first = await ToolVersionProbe.GetVersionAsync("yt-dlp");
        var second = await ToolVersionProbe.GetVersionAsync("yt-dlp");

        Assert.Equal("2026.08.19", first);
        Assert.Equal("2026.08.19", second);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Concurrent_callers_share_one_probe()
    {
        var calls = 0;
        var gate = new TaskCompletionSource<string?>();
        ToolVersionProbe.Resolver = (_, _) =>
        {
            Interlocked.Increment(ref calls);
            return gate.Task;
        };

        var pending = Enumerable.Range(0, 8).Select(_ => ToolVersionProbe.GetVersionAsync("yt-dlp")).ToArray();
        gate.SetResult("2026.08.19");
        var results = await Task.WhenAll(pending);

        Assert.Equal(1, calls);
        Assert.All(results, r => Assert.Equal("2026.08.19", r));
    }

    [Fact]
    public async Task A_missing_tool_is_not_cached_so_a_later_install_is_noticed()
    {
        var calls = 0;
        ToolVersionProbe.Resolver = (_, _) =>
        {
            var n = Interlocked.Increment(ref calls);
            return Task.FromResult<string?>(n == 1 ? null : "2026.08.19");
        };

        Assert.Null(await ToolVersionProbe.GetVersionAsync("yt-dlp"));
        Assert.Equal("2026.08.19", await ToolVersionProbe.GetVersionAsync("yt-dlp"));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task A_throwing_probe_returns_null_and_is_not_cached()
    {
        var calls = 0;
        ToolVersionProbe.Resolver = (_, _) =>
        {
            var n = Interlocked.Increment(ref calls);
            return n == 1
                ? Task.FromException<string?>(new InvalidOperationException("boom"))
                : Task.FromResult<string?>("3.0.23");
        };

        Assert.Null(await ToolVersionProbe.GetVersionAsync("vlc"));
        Assert.Equal("3.0.23", await ToolVersionProbe.GetVersionAsync("vlc"));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Tools_are_cached_independently_and_names_ignore_case()
    {
        ToolVersionProbe.Resolver = (tool, _) =>
            Task.FromResult<string?>(tool.ToLowerInvariant() == "vlc" ? "3.0.23" : "2026.08.19");

        Assert.Equal("3.0.23", await ToolVersionProbe.GetVersionAsync("vlc"));
        Assert.Equal("2026.08.19", await ToolVersionProbe.GetVersionAsync("yt-dlp"));
        Assert.Equal("3.0.23", await ToolVersionProbe.GetVersionAsync("VLC"));
    }

    [Fact]
    public async Task Invalidate_makes_the_next_question_probe_again()
    {
        var calls = 0;
        ToolVersionProbe.Resolver = (_, _) =>
            Task.FromResult<string?>(Interlocked.Increment(ref calls) == 1 ? "2026.08.19" : "2026.09.30");

        Assert.Equal("2026.08.19", await ToolVersionProbe.GetVersionAsync("yt-dlp"));
        ToolVersionProbe.Invalidate("yt-dlp");

        Assert.Equal("2026.09.30", await ToolVersionProbe.GetVersionAsync("yt-dlp"));
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData(2026, 8, 19, 0, "2026.08.19")]
    [InlineData(2026, 12, 1, 0, "2026.12.01")]
    [InlineData(2026, 8, 19, 123456, "2026.08.19.123456")]
    [InlineData(0, 0, 0, 0, null)]            // exe without version resources
    [InlineData(3, 0, 23, 0, null)]           // not a date: let the tool answer for itself
    [InlineData(2026, 13, 5, 0, null)]        // impossible month
    [InlineData(2026, 8, 0, 0, null)]         // impossible day
    public void Windows_file_versions_are_turned_into_yt_dlp_date_versions(
        int major, int minor, int build, int revision, string? expected)
        => Assert.Equal(expected, ToolVersionProbe.FormatCalVer(major, minor, build, revision));

    [Fact]
    public void FindOnPath_finds_an_exe_in_a_listed_folder_and_skips_bad_entries()
    {
        var dir = Path.Combine(Path.GetTempPath(), "nw-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var exe = Path.Combine(dir, "faketool.exe");
            File.WriteAllText(exe, "x");

            var path = string.Join(Path.PathSeparator, new[] { "", "Z:\\does-not-exist", dir });

            Assert.Equal(exe, ToolVersionProbe.FindOnPath("faketool", path));
            Assert.Equal(exe, ToolVersionProbe.FindOnPath("faketool.exe", path));
            Assert.Null(ToolVersionProbe.FindOnPath("othertool", path));
            Assert.Null(ToolVersionProbe.FindOnPath("faketool", null));
            Assert.Null(ToolVersionProbe.FindOnPath("", path));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}