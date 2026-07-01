using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Threading;
using NullWave.Models;
using Serilog;

namespace NullWave.Services;

public class ToastService
{
    public static ToastService Instance { get; } = new();
    public ObservableCollection<Toast> ActiveToasts { get; } = new();

    private ToastService() { }

    public void Show(string message, ToastType type = ToastType.Info, int durationMs = 3000)
    {
        Log.Debug("[ToastService] Showing {Type} Toast: {Message}", type, message);
        
        // FIX: Use positional record constructor instead of object initializer
        var toast = new Toast(message, type); 
        
        Dispatcher.UIThread.InvokeAsync(() => ActiveToasts.Add(toast));
        _ = Task.Run(async () =>
        {
            await Task.Delay(durationMs);
            await Dispatcher.UIThread.InvokeAsync(() => ActiveToasts.Remove(toast));
        });
    }

    public void Dismiss(Toast toast)
    {
        Dispatcher.UIThread.InvokeAsync(() => ActiveToasts.Remove(toast));
    }
}