using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NullWave.Models;

public class Track : INotifyPropertyChanged
{
    private string _title = string.Empty;
    private string _artist = string.Empty;
    private bool _isFavorite;
    private int _playCount;
    private DateTime? _lastPlayed;
    private string? _albumArtPath;
    private string? _notes;

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title
    {
        get => _title;
        set { if (_title != value) { _title = value; OnPropertyChanged(); } }
    }

    public string Artist
    {
        get => _artist;
        set { if (_artist != value) { _artist = value; OnPropertyChanged(); } }
    }

    public string? Url { get; set; }
    public string? FilePath { get; set; }
    public TrackSource Source { get; set; }
    public DateTime DateAdded { get; set; } = DateTime.Now;

    public bool IsFavorite
    {
        get => _isFavorite;
        set { if (_isFavorite != value) { _isFavorite = value; OnPropertyChanged(); } }
    }

    public int PlayCount
    {
        get => _playCount;
        set { if (_playCount != value) { _playCount = value; OnPropertyChanged(); } }
    }

    public DateTime? LastPlayed
    {
        get => _lastPlayed;
        set { if (_lastPlayed != value) { _lastPlayed = value; OnPropertyChanged(); } }
    }

    public List<string> Tags { get; set; } = new();

    public string? Notes
    {
        get => _notes;
        set { if (_notes != value) { _notes = value; OnPropertyChanged(); } }
    }

    public string? AlbumArtPath
    {
        get => _albumArtPath;
        set { if (_albumArtPath != value) { _albumArtPath = value; OnPropertyChanged(); } }
    }
}

    public enum TrackSource { 
            YouTube, 
            Spotify, 
            SoundCloud, 
            LastFm, 
            Local, 
            Unknown 
            }