using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NullWave.Plugins.Contracts;
using NullWave.Services;
using NullWave.Services.Plugins;
using Xunit;

namespace NullWave.Tests.Plugins;

public class PluginManagerInitializationTests
{
    private sealed class FakePlugin : IPlugin
    {
        private readonly Func<IPluginHost?, CancellationToken, Task<bool>> _init;
        private readonly Func<CancellationToken, Task> _shutdown;
        public int InitCalls;
        public int ShutdownCalls;

        public FakePlugin(
            string name,
            Func<IPluginHost?, CancellationToken, Task<bool>> init,
            bool enabled = true,
            Func<CancellationToken, Task>? shutdown = null)
        {
            Id = name;
            Name = name;
            _init = init;
            _shutdown = shutdown ?? (_ => Task.CompletedTask);
            IsEnabled = enabled;
        }

        public string Id { get; }
        public string Name { get; }
        public string Description => "fake";
        public PluginState State { get; set; } = PluginState.Unavailable;
        public bool IsEnabled { get; set; }

        public Task<bool> InitializeAsync(IPluginHost host, CancellationToken ct = default)
        {
            Interlocked.Increment(ref InitCalls);
            return _init(host, ct);
        }

        public Task ShutdownAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref ShutdownCalls);
            return _shutdown(ct);
        }
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
        var started = 0;
        var allStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<bool> WaitForTheOthers(IPluginHost? _, CancellationToken _2)
        {
            if (Interlocked.Increment(ref started) == 3) allStarted.SetResult();
            await allStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            return true;
        }

        var a = new FakePlugin("a", WaitForTheOthers);
        var b = new FakePlugin("b", WaitForTheOthers);
        var c = new FakePlugin("c", WaitForTheOthers);

        await NewManager(a, b, c).InitializeAllAsync(perPluginTimeout: TimeSpan.FromSeconds(10));

        Assert.All(new[] { a, b, c }, p => Assert.Equal(PluginState.Available, p.State));
        Assert.Equal(3, started);
    }

    [Fact]
    public async Task A_plugin_that_ignores_cancellation_is_marked_Error_and_does_not_hold_up_the_rest()
    {
        var stuck = new FakePlugin("stuck", (_, _) => new TaskCompletionSource<bool>().Task);
        var healthy = new FakePlugin("healthy", (_, _) => Task.FromResult(true));

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
        var cooperative = new FakePlugin("cooperative", async (_, ct) =>
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
        var broken = new FakePlugin("broken", (_, _) => throw new InvalidOperationException("boom"));
        var healthy = new FakePlugin("healthy", (_, _) => Task.FromResult(true));

        await NewManager(broken, healthy).InitializeAllAsync(perPluginTimeout: TimeSpan.FromSeconds(5));

        Assert.Equal(PluginState.Error, broken.State);
        Assert.Equal(PluginState.Available, healthy.State);
    }

    [Fact]
    public async Task A_plugin_that_reports_false_is_not_Available()
    {
        var notConfigured = new FakePlugin("no-key", (_, _) => Task.FromResult(false));
        await NewManager(notConfigured).InitializeAllAsync(perPluginTimeout: TimeSpan.FromSeconds(5));
        Assert.NotEqual(PluginState.Available, notConfigured.State);
    }

    [Fact]
    public async Task Disabled_plugins_are_not_initialized()
    {
        var disabled = new FakePlugin("off", (_, _) => Task.FromResult(true), enabled: false);
        await NewManager(disabled).InitializeAllAsync(perPluginTimeout: TimeSpan.FromSeconds(5));

        Assert.Equal(0, disabled.InitCalls);
        Assert.Equal(PluginState.Unavailable, disabled.State);
    }

    [Fact]
    public async Task Cancelling_startup_stops_waiting_and_does_not_throw()
    {
        var stuck = new FakePlugin("stuck", (_, _) => new TaskCompletionSource<bool>().Task);
        using var cts = new CancellationTokenSource();

        var run = NewManager(stuck).InitializeAllAsync(cts.Token, TimeSpan.FromSeconds(30));
        cts.CancelAfter(200);
        await run;

        Assert.Equal(PluginState.Unavailable, stuck.State);
    }

    [Fact]
    public async Task A_plugin_whose_start_code_blocks_does_not_block_the_caller()
    {
        using var release = new ManualResetEventSlim(false);
        var blocking = new FakePlugin("blocking", (_, _) =>
        {
            release.Wait(TimeSpan.FromSeconds(5));
            return Task.FromResult(true);
        });

        var start = NewManager(blocking).InitializeAllAsync(perPluginTimeout: TimeSpan.FromSeconds(10));

        Assert.False(start.IsCompleted, "The plugin's blocking work ran on the caller's thread");

        release.Set();
        await start;
        Assert.Equal(PluginState.Available, blocking.State);
    }

    [Fact]
    public async Task Concurrent_shutdown_requests_share_one_task_and_call_plugins_once()
    {
        var shutdownGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var plugin = new FakePlugin(
            "shutdown-once",
            (_, _) => Task.FromResult(true),
            shutdown: _ => shutdownGate.Task);
        var manager = NewManager(plugin);

        var firstShutdown = manager.ShutdownAllAsync();
        var secondShutdown = manager.ShutdownAllAsync();

        Assert.Same(firstShutdown, secondShutdown);
        Assert.Equal(1, plugin.ShutdownCalls);

        shutdownGate.SetResult();
        await Task.WhenAll(firstShutdown, secondShutdown);
    }
}

public class YtDlpDownloadProviderInitializationTests
{
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
        Assert.True(await provider.InitializeAsync(null!));
        Assert.Equal(PluginState.Available, provider.State);
    }

    [Fact]
    public async Task Missing_yt_dlp_makes_the_provider_Unavailable()
    {
        var provider = NewProvider(() => Task.FromResult<string?>(null));
        Assert.False(await provider.InitializeAsync(null!));
        Assert.Equal(PluginState.Unavailable, provider.State);
    }

    [Fact]
    public async Task A_disabled_provider_does_not_look_for_yt_dlp_at_all()
    {
        var probeCalls = 0;
        var provider = NewProvider(() => { probeCalls++; return Task.FromResult<string?>("2026.08.19"); }, enabled: false);

        Assert.False(await provider.InitializeAsync(null!));
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

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.InitializeAsync(null!, cts.Token));
        Assert.Equal(0, probeCalls);
    }
}