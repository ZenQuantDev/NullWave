using System;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NullWave.Services;

namespace NullWave.Models;

public partial class LiveNotification : ObservableObject
{
    public Guid Id { get; } = Guid.NewGuid();

    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _message = string.Empty;
    [ObservableProperty] private string _detailedMessage = string.Empty;
    [ObservableProperty] private string _scope = "Main";
    
    // Distinguishes between a quick fading toast and an ongoing live activity task
    [ObservableProperty] private bool _isLiveActivity; 
    
    // Tracks the open/closed state of the "More" expander drawer
    [ObservableProperty] private bool _isExpanded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSuccess))]
    [NotifyPropertyChangedFor(nameof(IsError))]
    [NotifyPropertyChangedFor(nameof(IsWarning))]
    [NotifyPropertyChangedFor(nameof(IsInfo))]
    [NotifyPropertyChangedFor(nameof(IconData))]
    [NotifyPropertyChangedFor(nameof(NotificationColor))]
    private ToastType _type = ToastType.Info;

    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private bool _isIndeterminate;
    [ObservableProperty] private bool _showProgressBar;
    [ObservableProperty] private bool _isCompleted;
    [ObservableProperty] private bool _isCancellable;

    public ICommand? CancelCommand { get; set; }
    public ICommand? ActionCommand { get; set; }
    [ObservableProperty] private string _actionButtonText = "View";
    [ObservableProperty] private bool _showActionButton;

    // Helper to conditionally show the "More" button in XAML
    public bool HasDetailedMessage => !string.IsNullOrWhiteSpace(DetailedMessage);

    [RelayCommand]
    private void ToggleExpand() => IsExpanded = !IsExpanded;

    public ICommand CloseCommand => new RelayCommand(() =>
    {
        if (CancelCommand != null)
            CancelCommand.Execute(null);
        else
            ToastService.Instance.Dismiss(this);
    });

    public bool IsSuccess => Type == ToastType.Success;
    public bool IsError => Type == ToastType.Error;
    public bool IsWarning => Type == ToastType.Warning;
    public bool IsInfo => Type == ToastType.Info;

    // Vector Path Data (Material StreamGeometry icons) used natively by Avalonia's <Path>
    public string IconData => Type switch
    {
        ToastType.Success => "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-2 15l-5-5 1.41-1.41L10 14.17l7.59-7.59L19 8l-9 9z",
        ToastType.Error   => "M12 2C6.47 2 2 6.47 2 12s4.47 10 10 10 10-4.47 10-10S17.53 2 12 2zm5 13.59L15.59 17 12 13.41 8.41 17 7 15.59 10.59 12 7 8.41 8.41 7 12 10.59 15.59 7 17 8.41 13.41 12 17 15.59z",
        ToastType.Warning => "M1 21h22L12 2 1 21zm12-3h-2v-2h2v2zm0-4h-2v-4h2v4z",
        _                 => "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-6h2v6zm0-8h-2V7h2v2z"
    };

    // Unified Accent Colors matching the notification types
    public string NotificationColor => Type switch
    {
        ToastType.Success => "#2ecc71", // Green
        ToastType.Error   => "#e74c3c", // Red
        ToastType.Warning => "#f39c12", // Orange
        _                 => "#3498db"  // Blue (Info)
    };
}