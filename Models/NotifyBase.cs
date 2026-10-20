using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NullWave.Models;

/// <summary>
/// Base class for models requiring property change notification.
/// Consolidates INPC implementation to reduce duplication across Track, Playlist, etc.
/// </summary>
public abstract class NotifyBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}