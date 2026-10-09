using System.Diagnostics;
using NullWave.Services;
using NullWave.Services.Plugins;

namespace NullWave.Tests.Plugins;

public class PluginManagerInitializationTests
{
    private sealed class FakePlugin : IPlugin
    {
        private readonly Func<CancellationToken, Task<bool>> _init;
        public int InitCalls;

        public FakePlugin(string name, Func<CancellationToken, Task<bool>> init, bool enabled = true)
        {
            Name = name;
            _init = init;
            IsEnabled = enabled;
        }

        public string Name { get; }
        public string Description => "fake";
        public PluginState State { get; set; } = PluginState.Unavailable;
        public bool IsEnabled { get; set; }

        public Task<bool> InitializeAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref InitCalls);
            return _init(ct);
        }

        public Task ShutdownAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private static PluginManager NewManager(params FakePlugin[] plugins)
    {
        var manager = new PluginManager();
        foreach (var plugin in plugins) manager.Register(plugin);
        return manager;
    }

    [Fact]
    public async Task Plugins_initialize_at_the_same_time_not_one_after_another()
    {
        async Task<bool> Slow(CancellationToken _) { await Task.Delay(400); return true; }
        var a = new FakePlugin("a", Slow);
        var b = new FakePlugin("b", Slow);
        var c = new FakePlugin("c", Slow);

        var clock = Stopwatch.StartNew();
        await NewManager(a, b, c).InitializeAllAsync(perPluginTimeout: TimeSpan.FromSeconds(10));
        clock.Stop();

        Assert.All(new[] { a, b, c }, p => Assert.Equal(PluginState.Available, p.State));
        Assert.True(clock.ElapsedMilliseconds < 1000,
            $"Took {clock.ElapsedMilliseconds} ms; three 400 ms plugins one after another would take 1200 ms or more");
    }

    [Fact]
    public async Task A_plugin_that_ignores_cancellation_is_marked_Error_and_does_not_hold_up_the_rest()
    {
        var stuck = new FakePlugin("stuck", _ => new TaskCompletionSource<bool>().Task);   // never completes
        var healthy = new FakePlugin("healthy", _ => Task.FromResult(true));

        var clock = Stopwatch.StartNew();
        await NewManager(stuck, healthy).InitializeAllAsync(perPluginTimeout: TimeSpan.FromMilliseconds(300));
        clock.Stop();

        Assert.Equal(PluginState.Error, stuck.State);
        Assert.Equal(PluginState.Available, healthy.State);
        Assert.True(clock.ElapsedMilliseconds < 3000, $"Took {clock.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task A_plugin_that_honours_the_cancellation_token_is_marked_Error_without_throwing()
    {
        var cooperative = new FakePlugin("cooperative", async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return true;
        });

        await NewManager(cooperative).InitializeAllAsync(perPluginTimeout: TimeSpan.FromMilliseconds(300));

        Assert.Equal(PluginState.Error, cooperative.State);
    }

    [Fact]
    public async Task A_throwing_plugin_is_marked_Error_and_the_others_still_initialize()
    {
        var broken = new FakePlugin("broken", _ => throw new InvalidOperationException("boom"));
        var healthy = new FakePlugin("healthy", _ => Task.FromResult(true));

        await NewManager(broken, healthy).InitializeAllAsync(perPluginTimeout: TimeSpan.FromSeconds(5));

        Assert.Equal(PluginState.Error, broken.State);
        Assert.Equal(PluginState.Available, healthy.State);
    }

    [Fact]
    public async Task A_plugin_that_reports_false_is_not_Available()
    {
        var notConfigured = new FakePlugin("no-key", _ => Task.FromResult(false));

        await NewManager(notConfigured).InitializeAllAsync(perPluginTimeout: TimeSpan.FromSeconds(5));

        Assert.NotEqual(PluginState.Available, notConfigured.State);
    }

    [Fact]
    public async Task Disabled_plugins_are_not_initialized()
    {
        var disabled = new FakePlugin("off", _ => Task.FromResult(true), enabled: false);

        await NewManager(disabled).InitializeAllAsync(perPluginTimeout: TimeSpan.FromSeconds(5));

        Assert.Equal(0, disabled.InitCalls);
        Assert.Equal(PluginState.Unavailable, disabled.State);
    }

    [Fact]
    public async Task Cancelling_startup_stops_waiting_and_does_not_throw()
    {
        var stuck = new FakePlugin("stuck", _ => new TaskCompletionSource<bool>().Task);
        using var cts = new CancellationTokenSource();

        var run = NewManager(stuck).InitializeAllAsync(cts.Token, TimeSpan.FromSeconds(30));
        cts.CancelAfter(200);
        await run;

        Assert.Equal(PluginState.Unavailable, stuck.State);
    }

    [Fact]
    public async Task A_plugin_whose_start_code_blocks_does_not_block_the_caller()
    {
        var blocking = new FakePlugin("blocking", _ =>
        {
            Thread.Sleep(500);          // synchronous work before returning, like a Process.Start + WaitForExit
            return Task.FromResult(true);
        });

        var clock = Stopwatch.StartNew();
        var start = NewManager(blocking).InitializeAllAsync(perPluginTimeout: TimeSpan.FromSeconds(5));
        var callReturnedAfterMs = clock.ElapsedMilliseconds;   // time until the call itself came back
        await start;

        Assert.True(callReturnedAfterMs < 250,
            $"The call took {callReturnedAfterMs} ms to return; the plugin's blocking work must run on a pool thread");
        Assert.Equal(PluginState.Available, blocking.State);
    }
}

public class YtDlpDownloadProviderInitializationTests
{
    // InitializeAsync never touches the inner DownloadService, so tests pass null for it.
    private static YtDlpDownloadProvider NewProvider(Func<Task<string?>> probe, bool enabled = true)
    {
        var prefs = new PreferencesService();
        prefs.Current.EnableYtDlp = enabled;
        return new YtDlpDownloadProvider(null!, prefs, probe);
    }

    [Fact]
    public async Task Found_version_makes_the_provider_Available()
    {
        var provider = NewProvider(() => Task.FromResult<string?>("2026.08.19"));

        Assert.True(await provider.InitializeAsync());
        Assert.Equal(PluginState.Available, provider.State);
    }

    [Fact]
    public async Task Missing_yt_dlp_makes_the_provider_Unavailable()
    {
        var provider = NewProvider(() => Task.FromResult<string?>(null));

        Assert.False(await provider.InitializeAsync());
        Assert.Equal(PluginState.Unavailable, provider.State);
    }

    [Fact]
    public async Task A_disabled_provider_does_not_look_for_yt_dlp_at_all()
    {
        var probeCalls = 0;
        var provider = NewProvider(() => { probeCalls++; return Task.FromResult<string?>("2026.08.19"); }, enabled: false);

        Assert.False(await provider.InitializeAsync());
        Assert.Equal(PluginState.Disabled, provider.State);
        Assert.Equal(0, probeCalls);
    }

    [Fact]
    public async Task A_cancelled_start_throws_before_probing()
    {
        var probeCalls = 0;
        var provider = NewProvider(() => { probeCalls++; return Task.FromResult<string?>("x"); });
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.InitializeAsync(cts.Token));
        Assert.Equal(0, probeCalls);
    }
}