using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NullWave.Models;

public class Playlist : INotifyPropertyChanged
{
    private bool _isPinned;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime DateCreated { get; set; } = DateTime.UtcNow;
    public List<Track> Tracks { get; set; } = new();

    /// <summary>
    /// UI-only state — not persisted on Playlist itself (the source of truth is
    /// Preferences.PinnedItems via NavigationViewModel). Set by PlaylistViewModel
    /// whenever the playlist list is refreshed or a pin/unpin happens, so rows
    /// bound to this property update live without needing a full list rebuild.
    /// </summary>
    public bool IsPinned
    {
        get => _isPinned;
        set { if (_isPinned != value) { _isPinned = value; OnPropertyChanged(); } }
    }
}