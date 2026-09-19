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
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(2);
    private readonly TimeSpan _threshold = TimeSpan.FromMilliseconds(500);

    public UiHangDetector()
    {
        Task.Run(async () =>
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    var sw = Stopwatch.StartNew();
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        sw.Stop();
                    });

                    if (sw.Elapsed > _threshold)
                    {
                        Log.Warning("[UiHangDetector] UI thread blocked for {ElapsedMs}ms", sw.ElapsedMilliseconds);
                    }
                    
                    await Task.Delay(_interval, _cts.Token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[UiHangDetector] Error in ping loop");
                }
            }
        });
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}