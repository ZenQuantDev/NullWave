using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NullWave.Models;

public partial class DownloadJob : ObservableObject
{
    public Guid Id { get; } = Guid.NewGuid();
    public string TrackId { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    
    [ObservableProperty] private string _title = "Unknown";
    [ObservableProperty] private string _artist = "Unknown";
    [ObservableProperty] private string _status = "Queued";
    [ObservableProperty] private double _progress;
    [ObservableProperty] private bool _isCompleted;
    [ObservableProperty] private bool _isFailed;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isIndeterminate = true;
    
    public Action? RetryAction { get; set; }
}