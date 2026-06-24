namespace NullWave.Models;

public enum ToastType
{
    Info,
    Success,
    Warning,
    Error
}

public class Toast
{
    public string Message { get; init; } = string.Empty;
    public ToastType Type { get; init; } = ToastType.Info;

    // Unified background colors based on your theme requirements
    public string BackgroundColor => Type switch
    {
        ToastType.Success => "#1E4620", // Deep green background
        ToastType.Error => "#5F1A1A",   // Deep red background
        ToastType.Warning => "#6A3B00", // Amber background
        _ => "#1E1E24"                  // BrushElevated dark background
    };

    // Unified accent border colors
    public string BorderColor => Type switch
    {
        ToastType.Success => "#4CAF50", // Vibrant green border
        ToastType.Error => "#F44336",   // Vibrant red border
        ToastType.Warning => "#FF9800", // Amber border
        _ => "#3F3F46"                  // BrushBorder default
    };
}