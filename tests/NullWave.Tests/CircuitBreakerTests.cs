using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using NullWave.Helpers;
using Xunit;

namespace NullWave.Tests;

public class CircuitBreakerTests
{
    [Fact]
    public async Task Opens_after_threshold_and_skips_requests_until_cooldown()
    {
        var clock = new FakeTimeProvider();
        var breaker = new CircuitBreaker("test", 2, TimeSpan.FromMinutes(1), clock);

        Assert.Null(await breaker.ExecuteAsync<string>(() => throw new HttpRequestException("offline")));
        Assert.Null(await breaker.ExecuteAsync<string>(() => throw new HttpRequestException("offline")));
        Assert.Equal(CircuitState.Open, breaker.State);

        var called = false;
        Assert.Null(await breaker.ExecuteAsync(() =>
        {
            called = true;
            return Task.FromResult("unexpected");
        }));
        Assert.False(called);

        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(CircuitState.HalfOpen, breaker.State);
    }

    [Fact]
    public async Task Half_open_allows_only_one_probe_and_success_closes_circuit()
    {
        var clock = new FakeTimeProvider();
        var breaker = new CircuitBreaker("test", 1, TimeSpan.FromSeconds(1), clock);
        await breaker.ExecuteAsync<string>(() => throw new HttpRequestException("offline"));
        clock.Advance(TimeSpan.FromSeconds(1));

        var probeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseProbe = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = breaker.ExecuteAsync(async () =>
        {
            probeStarted.SetResult();
            return await releaseProbe.Task;
        });
        await probeStarted.Task;

        Assert.Null(await breaker.ExecuteAsync(() => Task.FromResult("second probe")));
        releaseProbe.SetResult("recovered");
        Assert.Equal("recovered", await probe);
        Assert.Equal(CircuitState.Closed, breaker.State);
    }

    [Fact]
    public async Task User_cancellation_does_not_trip_the_circuit()
    {
        var breaker = new CircuitBreaker("test", 1);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => breaker.ExecuteAsync<string>(
            () => Task.FromException<string>(new OperationCanceledException(cancellation.Token)),
            cancellation.Token));

        Assert.Equal(CircuitState.Closed, breaker.State);
        Assert.Equal(0, breaker.FailureCount);
    }

    [Fact]
    public async Task Http_server_errors_trip_but_client_errors_do_not()
    {
        var breaker = new CircuitBreaker("test", 1);

        using var clientError = new HttpResponseMessage(HttpStatusCode.BadRequest);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await breaker.ExecuteHttpAsync(() => Task.FromResult(clientError)))?.StatusCode);
        Assert.Equal(CircuitState.Closed, breaker.State);

        using var serverError = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        Assert.Null(await breaker.ExecuteHttpAsync(() => Task.FromResult(serverError)));
        Assert.Equal(CircuitState.Open, breaker.State);
    }
}