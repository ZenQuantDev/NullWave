using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace NullWave.Helpers;

public enum CircuitState
{
    Closed,
    Open,
    HalfOpen
}

public sealed class CircuitBreaker
{
    private readonly object _gate = new();
    private readonly int _failureThreshold;
    private readonly TimeSpan _openDuration;
    private readonly TimeProvider _timeProvider;
    private int _failureCount;
    private DateTimeOffset _openedAt;
    private CircuitState _state;
    private bool _halfOpenProbeInFlight;

    public string Name { get; }
    public int FailureThreshold => _failureThreshold;

    public CircuitState State
    {
        get
        {
            lock (_gate)
            {
                UpdateStateForElapsedTime();
                return _state;
            }
        }
    }

    public int FailureCount
    {
        get { lock (_gate) return _failureCount; }
    }

    public TimeSpan? RetryAfter
    {
        get
        {
            lock (_gate)
            {
                UpdateStateForElapsedTime();
                if (_state != CircuitState.Open) return null;
                var remaining = _openDuration - (_timeProvider.GetUtcNow() - _openedAt);
                return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
            }
        }
    }

    public CircuitBreaker(
        string name,
        int failureThreshold = 3,
        TimeSpan? openDuration = null,
        TimeProvider? timeProvider = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A circuit name is required.", nameof(name));
        if (failureThreshold < 1) throw new ArgumentOutOfRangeException(nameof(failureThreshold));

        Name = name;
        _failureThreshold = failureThreshold;
        _openDuration = openDuration ?? TimeSpan.FromMinutes(15);
        if (_openDuration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(openDuration));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<T?> ExecuteAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryBeginRequest(out var isHalfOpenProbe))
        {
            Log.Debug("[CircuitBreaker:{Name}] Circuit is open; skipping request", Name);
            return default;
        }

        try
        {
            var result = await action().ConfigureAwait(false);
            RecordSuccess();
            return result;
        }
        catch (Exception ex) when (IsTransientFailure(ex, cancellationToken))
        {
            RecordFailure(ex);
            return default;
        }
        catch
        {
            ReleaseHalfOpenProbe(isHalfOpenProbe);
            throw;
        }
    }

    public Task<HttpResponseMessage?> ExecuteHttpAsync(
        Func<Task<HttpResponseMessage>> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteAsync(async () =>
        {
            var response = await request().ConfigureAwait(false);
            if (IsTransientStatusCode(response.StatusCode))
            {
                var statusCode = response.StatusCode;
                response.Dispose();
                throw new HttpRequestException($"Transient HTTP status {(int)statusCode}.", null, statusCode);
            }
            return response;
        }, cancellationToken);
    }

    private bool TryBeginRequest(out bool isHalfOpenProbe)
    {
        lock (_gate)
        {
            UpdateStateForElapsedTime();
            isHalfOpenProbe = _state == CircuitState.HalfOpen;
            if (_state == CircuitState.Open) return false;
            if (isHalfOpenProbe)
            {
                if (_halfOpenProbeInFlight) return false;
                _halfOpenProbeInFlight = true;
            }
            return true;
        }
    }

    private void UpdateStateForElapsedTime()
    {
        if (_state != CircuitState.Open || _timeProvider.GetUtcNow() - _openedAt < _openDuration) return;
        _state = CircuitState.HalfOpen;
        _halfOpenProbeInFlight = false;
        Log.Information("[CircuitBreaker:{Name}] Cooldown elapsed; allowing one probe", Name);
    }

    private void RecordSuccess()
    {
        lock (_gate)
        {
            if (_state != CircuitState.Closed || _failureCount != 0)
                Log.Information("[CircuitBreaker:{Name}] Request succeeded; circuit closed", Name);
            _state = CircuitState.Closed;
            _failureCount = 0;
            _halfOpenProbeInFlight = false;
        }
    }

    private void RecordFailure(Exception exception)
    {
        lock (_gate)
        {
            _failureCount++;
            Log.Warning(exception, "[CircuitBreaker:{Name}] Transient request failure {Count}/{Threshold}",
                Name, _failureCount, _failureThreshold);

            if (_state == CircuitState.HalfOpen || _failureCount >= _failureThreshold)
            {
                _state = CircuitState.Open;
                _openedAt = _timeProvider.GetUtcNow();
                _halfOpenProbeInFlight = false;
                Log.Error("[CircuitBreaker:{Name}] Circuit opened for {Duration}", Name, _openDuration);
            }
        }
    }

    private void ReleaseHalfOpenProbe(bool isHalfOpenProbe)
    {
        if (!isHalfOpenProbe) return;
        lock (_gate) _halfOpenProbeInFlight = false;
    }

    private static bool IsTransientFailure(Exception exception, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return false;
        return exception is HttpRequestException or TimeoutException or OperationCanceledException;
    }

    private static bool IsTransientStatusCode(HttpStatusCode statusCode)
    {
        var numericStatus = (int)statusCode;
        return statusCode is HttpStatusCode.RequestTimeout or (HttpStatusCode)429 || numericStatus >= 500;
    }
}

public static class CircuitBreakerRegistry
{
    private static readonly ConcurrentDictionary<string, CircuitBreaker> Breakers = new(StringComparer.OrdinalIgnoreCase);

    public static CircuitBreaker Get(string name, int failureThreshold = 3, TimeSpan? openDuration = null)
        => Breakers.GetOrAdd(name, key => new CircuitBreaker(key, failureThreshold, openDuration));

    public static IReadOnlyList<CircuitBreaker> GetAll()
        => Breakers.Values.OrderBy(breaker => breaker.Name, StringComparer.OrdinalIgnoreCase).ToArray();
}