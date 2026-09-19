using System;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;
using NullWave.Services;

namespace NullWave.Models;

public enum ToastType
{
    Info,
    Success,
    Warning,
    Error
}

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
    [ObservableProperty] private bool _isDismissing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSuccess))]
    [NotifyPropertyChangedFor(nameof(IsError))]
    [NotifyPropertyChangedFor(nameof(IsWarning))]
    [NotifyPropertyChangedFor(nameof(IsInfo))]
    [NotifyPropertyChangedFor(nameof(IconData))]
    [NotifyPropertyChangedFor(nameof(IconKind))]
    [NotifyPropertyChangedFor(nameof(NotificationTint))]
    [NotifyPropertyChangedFor(nameof(NotificationBrush))]
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

    public MaterialIconKind IconKind => Type switch
    {
        ToastType.Success => MaterialIconKind.CheckCircle,
        ToastType.Error   => MaterialIconKind.AlertCircle,
        ToastType.Warning => MaterialIconKind.Alert,
        _                 => MaterialIconKind.InformationOutline
    };

    /// <summary>
    /// Traverses application theme resources to resolve dynamic brushes correctly.
    /// Falls back to a default solid color brush if resource key resolution fails.
    /// </summary>
    private static IBrush GetThemeBrush(string resourceKey, string fallbackHex)
    {
        if (Application.Current != null &&
            Application.Current.TryGetResource(resourceKey, Application.Current.ActualThemeVariant, out var res) &&
            res is IBrush brush)
        {
            return brush;
        }
        return new SolidColorBrush(Color.Parse(fallbackHex));
    }

    public IBrush NotificationTint => Type switch
    {
        ToastType.Success => GetThemeBrush("BrushGreenDim", "#2010B981"),
        ToastType.Error   => GetThemeBrush("BrushRedDim",   "#20EF4444"),
        ToastType.Warning => GetThemeBrush("BrushAmberDim", "#20F59E0B"),
        _                 => GetThemeBrush("BrushBlueDim",  "#203B82F6")
    };

    public IBrush NotificationBrush => Type switch
    {
        ToastType.Success => GetThemeBrush("BrushGreen", "#10B981"),
        ToastType.Error   => GetThemeBrush("BrushRed",   "#EF4444"),
        ToastType.Warning => GetThemeBrush("BrushAmber", "#F59E0B"),
        _                 => GetThemeBrush("BrushBlue",  "#3B82F6")
    };
}