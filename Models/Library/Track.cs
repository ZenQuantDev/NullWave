using System;
using System.Collections.Generic;
using NullWave.Helpers;

namespace NullWave.Models;

public class Track : NotifyBase
{
    private string _title = string.Empty;
    private string _artist = "Unknown";
    private string? _album;
    private int _trackNumber;
    private TimeSpan _duration;
    private string? _url;
    private string? _filePath;
    private string? _albumArtPath;
    private TrackSource _source = TrackSource.Unknown;
    private MediaType _mediaType = MediaType.Music;
    private bool _isFavorite;
    private int _playCount;
    private DateTime? _lastPlayed;
    private int _skipCount;
    private DateTime? _lastSkipped;
    private DateTime _dateAdded = DateTime.UtcNow;
    private long _playbackPositionTicks;
    private List<string> _tags = new();
    private string? _notes;
    private bool _titleForceCleaned;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title { get => _title; set => SetField(ref _title, value); }
    public string Artist { get => _artist; set => SetField(ref _artist, value); }
    public string? Album { get => _album; set => SetField(ref _album, value); }
    public int TrackNumber { get => _trackNumber; set => SetField(ref _trackNumber, value); }
    
    public TimeSpan Duration 
    { 
        get => _duration; 
        set 
        { 
            if (SetField(ref _duration, value)) 
                OnPropertyChanged(nameof(DisplayDuration)); 
        } 
    }
    
    public string? Url { get => _url; set => SetField(ref _url, value); }
    public string? FilePath { get => _filePath; set => SetField(ref _filePath, value); }
    public string? AlbumArtPath { get => _albumArtPath; set => SetField(ref _albumArtPath, value); }
    public TrackSource Source { get => _source; set => SetField(ref _source, value); }
    
    public MediaType MediaType 
    { 
        get => _mediaType; 
        set 
        { 
            if (SetField(ref _mediaType, value))
            {
                // Notify UI that computed boolean properties have changed
                OnPropertyChanged(nameof(IsRadio));
                OnPropertyChanged(nameof(IsAudiobook));
                OnPropertyChanged(nameof(IsMusic));
            }
        } 
    }

    // Computed properties required by XAML bindings
    public bool IsRadio => _mediaType == MediaType.Radio;
    public bool IsAudiobook => _mediaType == MediaType.Audiobook;
    public bool IsMusic => _mediaType == MediaType.Music;

    public bool IsFavorite { get => _isFavorite; set => SetField(ref _isFavorite, value); }
    public int PlayCount { get => _playCount; set => SetField(ref _playCount, value); }
    public DateTime? LastPlayed { get => _lastPlayed; set => SetField(ref _lastPlayed, value); }
    public int SkipCount { get => _skipCount; set => SetField(ref _skipCount, value); }
    public DateTime? LastSkipped { get => _lastSkipped; set => SetField(ref _lastSkipped, value); }
    public DateTime DateAdded { get => _dateAdded; set => SetField(ref _dateAdded, value); }
    public long PlaybackPositionTicks { get => _playbackPositionTicks; set => SetField(ref _playbackPositionTicks, value); }
    public List<string> Tags { get => _tags; set => SetField(ref _tags, value); }
    public string? Notes { get => _notes; set => SetField(ref _notes, value); }
    public bool TitleForceCleaned { get => _titleForceCleaned; set => SetField(ref _titleForceCleaned, value); }

    public string DisplayDuration => Duration.ToString(@"m\:ss");
}