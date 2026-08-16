using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using NullWave.Models;

namespace NullWave.Services;

/// <summary>
/// Central notification hub.
/// Features: single-host routing (MainWindow vs SettingsWindow), scope grouping,
/// hover-pause, dismiss-all, hard cap of 4 visible toasts, actionable buttons.
/// </summary>
public class ToastService : INotifyPropertyChanged
{
    public static ToastService Instance { get; } = new();

    public ObservableCollection<LiveNotification> ActiveToasts { get; } = new();
    // UI bridge for XAML that binds via ActiveNotifications
    public ObservableCollection<LiveNotification> ActiveNotifications => ActiveToasts;

    public const int MaxVisibleToasts = 4;

    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _timers = new();

    //  Single-host routing: only ONE window renders toasts at a time 
    private bool _settingsHostActive;
    public bool SettingsHostActive
    {
        get => _settingsHostActive;
        set
        {
            if (_settingsHostActive != value)
            {
                _settingsHostActive = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Called by SettingsWindow on Opened/Closed.</summary>
    public void SetActiveHost(bool settingsWindowActive) => SettingsHostActive = settingsWindowActive;

    //  Dismiss-all pill 
    public bool ShowDismissAll => ActiveToasts.Count > 2;
    public RelayCommand DismissAllCommand { get; }

    private ToastService()
    {
        DismissAllCommand = new RelayCommand(() =>
        {
            foreach (var t in ActiveToasts.ToList()) Dismiss(t);
        });
        ActiveToasts.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ShowDismissAll));
    }

    //  Pathway 1: static toast 
    public LiveNotification ShowToast(string message, ToastType type = ToastType.Info, string title = "", string detailedMessage = "")
        => Show(message, type, 7000, title, detailedMessage);

    //  Pathway 2: live activity (scope = reuse existing instead of stacking) 
    public LiveNotification StartLiveActivity(string title, string initialMessage, bool isIndeterminate = true, string? scope = null)
    {
        if (scope != null)
        {
            var existing = ActiveToasts.FirstOrDefault(t => t.IsLiveActivity && t.Scope == scope);
            if (existing != null)
            {
                UpdateLiveActivity(existing, initialMessage, 0, isIndeterminate);
                return existing;
            }
        }

        var notification = new LiveNotification
        {
            Title = title,
            Message = initialMessage,
            Scope = scope ?? "Main",
            Type = ToastType.Info,
            IsLiveActivity = true,
            ShowProgressBar = true,
            IsIndeterminate = isIndeterminate,
            ProgressValue = 0
        };
        AddToast(notification);
        return notification;
    }

    public void UpdateLiveActivity(LiveNotification? notification, string? message = null, double? progressValue = null, bool? isIndeterminate = null)
    {
        if (notification == null) return;

        void Apply()
        {
            if (message != null) notification.Message = message;
            if (progressValue.HasValue) notification.ProgressValue = progressValue.Value;
            if (isIndeterminate.HasValue) notification.IsIndeterminate = isIndeterminate.Value;
        }

        if (Dispatcher.UIThread.CheckAccess()) Apply();
        else Dispatcher.UIThread.Post(Apply);
    }

    public void CompleteLiveActivity(LiveNotification? notification, string finalMessage,
        int lingerMs = 5000, ToastType finalType = ToastType.Success,
        string? actionText = null, Action? actionCallback = null)
    {
        if (notification == null) return;

        void Apply()
        {
            notification.Message = finalMessage;
            notification.Type = finalType;
            notification.IsIndeterminate = false;
            notification.ProgressValue = 100;
            notification.IsCompleted = true;

            if (!string.IsNullOrEmpty(actionText) && actionCallback != null)
            {
                notification.ShowActionButton = true;
                notification.ActionButtonText = actionText;
                notification.ActionCommand = new RelayCommand(() => { actionCallback(); Dismiss(notification); });
            }
            else
            {
                notification.ShowActionButton = false;
                notification.ActionCommand = null;
            }
        }

        if (Dispatcher.UIThread.CheckAccess()) Apply();
        else Dispatcher.UIThread.Post(Apply);

        ScheduleDismiss(notification, lingerMs);
    }

    //  Pathway 3: grouped + actionable toast 
    public LiveNotification Show(
        string message, ToastType type = ToastType.Info, int durationMs = 4000,
        string title = "", string detailedMessage = "",
        string? actionText = null, Action? actionCallback = null, string? scope = null)
    {
        var existing = !string.IsNullOrEmpty(scope)
            ? ActiveToasts.FirstOrDefault(t => t.Scope == scope)
            : null;

        var notification = existing ?? new LiveNotification
        {
            Scope = scope ?? "Main",
            IsLiveActivity = false,
            ShowProgressBar = false
        };

        notification.Title = string.IsNullOrWhiteSpace(title) ? type.ToString() : title;
        notification.Message = message;
        notification.DetailedMessage = detailedMessage;
        notification.Type = type;

        if (!string.IsNullOrEmpty(actionText) && actionCallback != null)
        {
            notification.ShowActionButton = true;
            notification.ActionButtonText = actionText;
            notification.ActionCommand = new RelayCommand(() => { actionCallback(); Dismiss(notification); });
        }
        else
        {
            notification.ShowActionButton = false;
            notification.ActionCommand = null;
        }

        if (existing == null) AddToast(notification);
        ScheduleDismiss(notification, durationMs); // resets countdown on reuse
        return notification;
    }

    //  Hover-pause 
    public void PauseAutoDismiss(LiveNotification n)
    {
        if (_timers.TryGetValue(n.Id, out var cts)) cts.Cancel();
    }

    public void ResumeAutoDismiss(LiveNotification n, int ms = 4000) => ScheduleDismiss(n, ms);

    //  Internals 
    private void AddToast(LiveNotification n)
    {
        void Add() { ActiveToasts.Add(n); EnforceCap(); }
        if (Dispatcher.UIThread.CheckAccess()) Add();
        else Dispatcher.UIThread.Post(Add);
    }

    private void EnforceCap()
    {
        while (ActiveToasts.Count > MaxVisibleToasts)
        {
            var victim = ActiveToasts.FirstOrDefault(t => !t.IsLiveActivity) ?? ActiveToasts[0];
            if (victim != null) Dismiss(victim);
        }
    }

    private void ScheduleDismiss(LiveNotification n, int ms)
    {
        if (_timers.TryRemove(n.Id, out var old)) old.Cancel();
        var cts = new CancellationTokenSource();
        _timers[n.Id] = cts;
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(ms, cts.Token); Dismiss(n); }
            catch (TaskCanceledException) { }
        });
    }

    public void Dismiss(LiveNotification notification)
    {
        if (notification == null) return;
        if (_timers.TryRemove(notification.Id, out var cts)) cts.Cancel();

        void Remove()
        {
            if (ActiveToasts.Contains(notification)) ActiveToasts.Remove(notification);
        }
        if (Dispatcher.UIThread.CheckAccess()) Remove();
        else Dispatcher.UIThread.Post(Remove);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}