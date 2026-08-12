using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace NullWave.Models;

public class Playlist : INotifyPropertyChanged
{
    private bool _isPinned;
    private Guid? _folderId;
    private string? _customArtPath;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime DateCreated { get; set; } = DateTime.UtcNow;
    public List<Track> Tracks { get; set; } = new();

    /// <summary>User-chosen cover image path (null = auto from first track).</summary>
    public string? CustomArtPath
    {
        get => _customArtPath;
        set { if (_customArtPath != value) { _customArtPath = value; OnPropertyChanged(); OnPropertyChanged(nameof(ArtPath)); } }
    }

    /// <summary>Resolved sidebar art: custom cover first, else first track's album art.</summary>
    public string? ArtPath => CustomArtPath ?? Tracks.FirstOrDefault()?.AlbumArtPath;

    public bool IsPinned
    {
        get => _isPinned;
        set { if (_isPinned != value) { _isPinned = value; OnPropertyChanged(); } }
    }

    public Guid? FolderId
    {
        get => _folderId;
        set { if (_folderId != value) { _folderId = value; OnPropertyChanged(); } }
    }
}

public class PlaylistFolder : INotifyPropertyChanged
{
    private bool _isExpanded = true;
    private string _name = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name
    {
        get => _name;
        set { if (_name != value) { _name = value; OnPropertyChanged(); } }
    }
    public DateTime DateCreated { get; set; } = DateTime.UtcNow;

    public bool IsExpanded
    {
        get => _isExpanded;
        set { if (_isExpanded != value) { _isExpanded = value; OnPropertyChanged(); } }
    }
}