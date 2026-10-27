using System;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace NullWave.Services.Plugins;

/// <summary>
/// Wraps every call through a registered provider with timeout + try/catch + logging.
/// This is the real fault isolation boundary — not assembly loading.
/// </summary>
public static class ProviderCaller
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(8);

    public static async Task<T?> CallAsync<T>(
        IProvider provider,
        Func<CancellationToken, Task<T?>> action,
        TimeSpan? timeout = null,
        CancellationToken ct = default) where T : class
    {
        var effectiveTimeout = timeout ?? DefaultTimeout;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(effectiveTimeout);

        try
        {
            return await action(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            Log.Warning("[ProviderCaller] Provider {Id} timed out after {Seconds}s",
                provider.Descriptor.Id, effectiveTimeout.TotalSeconds);
            return null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[ProviderCaller] Provider {Id} threw", provider.Descriptor.Id);
            return null;
        }
    }
}