using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace NullWave.Models;

public class Playlist : NotifyBase
{
    private bool _isPinned;
    private Guid? _folderId;
    private string? _customArtPath;

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime DateCreated { get; set; } = DateTime.UtcNow;
    
    public ObservableCollection<Track> Tracks { get; set; } = new();
    public Dictionary<Guid, DateTime> TrackDateAdded { get; set; } = new();

    public DateTime GetDateAdded(Guid trackId) =>
        TrackDateAdded.TryGetValue(trackId, out var d) && d != default ? d : DateCreated;

    public string? CustomArtPath
    {
        get => _customArtPath;
        set { if (SetField(ref _customArtPath, value)) OnPropertyChanged(nameof(ArtPath)); }
    }

    public string? ArtPath => CustomArtPath ?? Tracks.FirstOrDefault()?.AlbumArtPath;

    public bool IsPinned
    {
        get => _isPinned;
        set => SetField(ref _isPinned, value);
    }

    public Guid? FolderId
    {
        get => _folderId;
        set => SetField(ref _folderId, value);
    }
}