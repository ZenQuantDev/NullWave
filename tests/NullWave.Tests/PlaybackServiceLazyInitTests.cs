using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NullWave.Services;
using Xunit;

namespace NullWave.Tests.Playback;

public class PlaybackServiceLazyInitTests
{
    private sealed class Harness : IDisposable
    {
        public int FactoryCalls;
        public readonly List<PlaybackState> States = new();
        public readonly List<string> Failures = new();
        public readonly PlaybackService Service;

        public Harness(string? factoryError = "no vlc here")
        {
            Service = new PlaybackService(
                engineFactory: () =>
                {
                    Interlocked.Increment(ref FactoryCalls);
                    throw new InvalidOperationException(factoryError);
                },
                uiPost: action => action());

            Service.StateChanged += States.Add;
            Service.StreamFailed += Failures.Add;
        }

        public void Dispose() => Service.Dispose();
    }

    private static async Task WaitUntil(Func<bool> condition, int timeoutMs = 3000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition() && Environment.TickCount64 < deadline)
            await Task.Delay(10);
    }

    [Fact]
    public void Constructing_the_service_does_not_start_the_engine()
    {
        using var h = new Harness();
        Assert.Equal(0, h.FactoryCalls);
        Assert.False(h.Service.IsEngineReady);
        Assert.Null(h.Service.EngineError);
    }

    [Fact]
    public void Everything_that_needs_a_player_is_a_safe_no_op_before_the_engine_exists()
    {
        using var h = new Harness();
        Assert.False(h.Service.IsPlaying);
        Assert.False(h.Service.IsPaused);
        Assert.Equal(TimeSpan.Zero, h.Service.Position);
        Assert.Equal(TimeSpan.Zero, h.Service.Duration);

        h.Service.Pause(); h.Service.Resume(); h.Service.Stop(); h.Service.Seek(0.5f); h.Service.SetRate(1.5f);

        Assert.Equal(0, h.FactoryCalls);
        Assert.False(h.Service.IsEngineReady);
    }

    [Fact]
    public async Task Fades_before_the_engine_exists_return_without_starting_it()
    {
        using var h = new Harness();
        await h.Service.FadeAndPauseAsync(200);
        await h.Service.FadeAndResumeAsync(200);
        Assert.Equal(0, h.FactoryCalls);
    }

    [Fact]
    public void Volume_set_before_the_engine_exists_is_remembered()
    {
        using var h = new Harness();
        h.Service.Volume = 0.5f;
        Assert.Equal(0.5f, h.Service.Volume, precision: 2);
        Assert.Equal(0, h.FactoryCalls);
    }

    [Fact]
    public void ResyncState_before_the_engine_exists_reports_Stopped()
    {
        using var h = new Harness();
        h.Service.ResyncState();
        Assert.Equal(new[] { PlaybackState.Stopped }, h.States);
        Assert.Equal(0, h.FactoryCalls);
    }

    [Fact]
    public async Task Play_with_a_failing_engine_reports_the_reason_and_does_not_throw()
    {
        using var h = new Harness("libvlc.dll not found");
        h.Service.Play("song.mp3");

        await WaitUntil(() => h.Failures.Count > 0);

        Assert.False(h.Service.IsEngineReady);
        Assert.Equal("libvlc.dll not found", h.Service.EngineError);
        var message = Assert.Single(h.Failures);
        Assert.Contains("Audio engine unavailable", message);
        Assert.Contains("libvlc.dll not found", message);
        Assert.Contains(PlaybackState.Stopped, h.States);
    }

    [Fact]
    public async Task A_failed_start_is_remembered_and_not_retried_on_every_click()
    {
        using var h = new Harness();
        h.Service.Play("a.mp3");
        h.Service.Play("b.mp3");
        h.Service.Play("c.mp3");

        await WaitUntil(() => h.Failures.Count > 0);

        Assert.Equal(1, h.FactoryCalls);
        Assert.Single(h.Failures);   
    }

    [Fact]
    public async Task Crossfade_with_a_failing_engine_returns_without_throwing()
    {
        using var h = new Harness();
        var generation = await h.Service.CrossfadeToAsync("next.mp3", 1000, 0.8f);
        Assert.Equal(h.Service.CrossfadeGeneration, generation);
        Assert.False(h.Service.IsCrossfading);
    }

    [Fact]
    public async Task WarmUp_starts_the_engine_once_however_often_it_is_called()
    {
        using var h = new Harness();
        await Task.WhenAll(h.Service.WarmUpAsync(), h.Service.WarmUpAsync(), h.Service.WarmUpAsync());
        h.Service.Play("a.mp3");

        await WaitUntil(() => h.Service.EngineError != null);

        Assert.Equal(1, h.FactoryCalls);
    }

    [Fact]
    public async Task Disposing_before_the_engine_started_is_safe_and_blocks_a_later_start()
    {
        var h = new Harness();
        h.Service.Dispose();
        h.Service.Play("a.mp3");

        await WaitUntil(() => h.Failures.Count > 0);

        Assert.Equal(0, h.FactoryCalls);
        Assert.Contains("shut down", Assert.Single(h.Failures));
        h.Service.Dispose();
    }
}