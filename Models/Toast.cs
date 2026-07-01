using System;

namespace NullWave.Models;

public enum ToastType
{
    Info,
    Success,
    Warning,
    Error
}

public record Toast(string Message, ToastType Type = ToastType.Info)
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string BackgroundColor => Type switch
    {
        ToastType.Success => "#1E4620",
        ToastType.Error   => "#5F1A1A",
        ToastType.Warning => "#6A3B00",
        _                 => "#1E1E24"
    };

    public string BorderColor => Type switch
    {
        ToastType.Success => "#4CAF50",
        ToastType.Error   => "#F44336",
        ToastType.Warning => "#FF9800",
        _                 => "#3F3F46"
    };
}