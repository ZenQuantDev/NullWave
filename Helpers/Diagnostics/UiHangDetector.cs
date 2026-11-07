using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Serilog;

namespace NullWave.Helpers;

public class UiHangDetector : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private long _lastPong = Stopwatch.GetTimestamp();
    private int _pending;
    private bool _reported;

    public UiHangDetector()
    {
        var ct = _cts.Token;
        Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (Interlocked.Exchange(ref _pending, 1) == 0)
                    {
                        Dispatcher.UIThread.Post(() => 
                        { 
                            Interlocked.Exchange(ref _lastPong, Stopwatch.GetTimestamp()); 
                            Volatile.Write(ref _pending, 0); 
                        });
                    }
                    
                    await Task.Delay(500, ct);
                    
                    var ms = (long)Stopwatch.GetElapsedTime(Interlocked.Read(ref _lastPong)).TotalMilliseconds;
                    if (ms > 2000 && !_reported) 
                    { 
                        _reported = true; 
                        Log.Warning("[UiHangDetector] UI thread STILL blocked after {Ms} ms", ms); 
                    }
                    else if (ms <= 2000 && _reported) 
                    { 
                        _reported = false; 
                        Log.Information("[UiHangDetector] UI thread recovered"); 
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) 
                { 
                    Log.Error(ex, "[UiHangDetector] ping loop"); 
                    try { await Task.Delay(5000, ct); } catch { break; }
                }
            }
        }, ct);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}