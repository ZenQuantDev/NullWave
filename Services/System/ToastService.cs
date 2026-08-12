using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Threading;
using NullWave.Models;

namespace NullWave.Services;

public class ToastService
{
    public static ToastService Instance { get; } = new();

    public ObservableCollection<LiveNotification> ActiveToasts { get; } = new();

    // UI Bridge: Keeps both your ViewModels and direct XAML bindings happy
    public ObservableCollection<LiveNotification> ActiveNotifications => ActiveToasts;

    private ToastService() { }

    /// <summary>
    /// Pathway 1: Transitory Single Informer Notification.
    /// Pops up to deliver a static message, hooks up an optional detailed drawer, and vanishes.
    /// </summary>
    public LiveNotification ShowToast(string message, ToastType type = ToastType.Info, string title = "", string detailedMessage = "")
    {
        var notification = new LiveNotification
        {
            Title = string.IsNullOrWhiteSpace(title) ? type.ToString() : title,
            Message = message,
            DetailedMessage = detailedMessage,
            Type = type,
            IsLiveActivity = false,
            ShowProgressBar = false
        };

        Dispatcher.UIThread.Post(() => ActiveToasts.Add(notification));

        // Auto-destruct timer set to a clean 7-second reading window
        _ = Task.Run(async () =>
        {
            await Task.Delay(7000);
            Dismiss(notification);
        });

        return notification;
    }

    /// <summary>
    /// Pathway 2: Android-style Live Updating Notification.
    /// Persistent, trackable, and displays progress metrics for heavy operations.
    /// </summary>
    public LiveNotification StartLiveActivity(string title, string initialMessage, bool isIndeterminate = true)
    {
        var notification = new LiveNotification
        {
            Title = title,
            Message = initialMessage,
            Type = ToastType.Info,
            IsLiveActivity = true,
            ShowProgressBar = true,
            IsIndeterminate = isIndeterminate,
            ProgressValue = 0
        };

        Dispatcher.UIThread.Post(() => ActiveToasts.Add(notification));
        return notification;
    }

    /// <summary>
    /// Thread-safe progress update for an in-flight live activity. DownloadService's
    /// progress/batch events fire from Process callback threads, not the UI thread,
    /// so every property write here has to go through the dispatcher.
    /// </summary>
    public void UpdateLiveActivity(LiveNotification? notification, string? message = null, double? progressValue = null, bool? isIndeterminate = null)
    {
        if (notification == null) return;

        void Apply()
        {
            if (message != null) notification.Message = message;
            if (progressValue.HasValue) notification.ProgressValue = progressValue.Value;
            if (isIndeterminate.HasValue) notification.IsIndeterminate = isIndeterminate.Value;
        }

        if (Dispatcher.UIThread.CheckAccess())
            Apply();
        else
            Dispatcher.UIThread.Post(Apply);
    }

    /// <summary>
    /// Finishes a live activity IN PLACE: the same toast shows the final message,
    /// switches to the final type (color + icon), holds briefly, then dismisses.
    /// Never spawns a second toast.
    /// </summary>
    public void CompleteLiveActivity(LiveNotification? notification, string finalMessage,
        int lingerMs = 2500, ToastType finalType = ToastType.Success)
    {
        if (notification == null) return;

        void Apply()
        {
            notification.Message = finalMessage;
            notification.Type = finalType;        // morphs bar color + icon via INPC
            notification.IsIndeterminate = false;
            notification.ProgressValue = 100;
            notification.IsCompleted = true;
        }

        if (Dispatcher.UIThread.CheckAccess()) Apply();
        else Dispatcher.UIThread.Post(Apply);

        _ = Task.Run(async () =>
        {
            await Task.Delay(lingerMs);
            Dismiss(notification);
        });
    }

    /// <summary>
    /// Temporary Backward Compatibility Wrapper.
    /// Prevents compilation failures elsewhere in the app while we refactor step-by-step.
    /// </summary>
    public LiveNotification Show(string message, ToastType type = ToastType.Info, int durationMs = 3000)
    {
        return ShowToast(message, type, title: type.ToString());
    }

    public void Dismiss(LiveNotification notification)
    {
        if (notification == null) return;

        // Ensure UI modifications always run on the dispatcher thread safely
        if (Dispatcher.UIThread.CheckAccess())
        {
            if (ActiveToasts.Contains(notification))
                ActiveToasts.Remove(notification);
        }
        else
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (ActiveToasts.Contains(notification))
                    ActiveToasts.Remove(notification);
            });
        }
    }
}