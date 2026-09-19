using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using NullWave.Helpers;
using SQLite;

namespace NullWave.Models;

/*
 * DEV NOTE: Why manual INotifyPropertyChanged instead of [ObservableProperty]?
 * Track is our core database entity. It gets passed to background threads,
 * serialized, and mapped from SQLite records constantly. CommunityToolkit's
 * source generators are great for ViewModels, but for pure data models that
 * cross thread boundaries, manual INPC is slightly safer and prevents
 * accidental UI bindings from triggering heavy database updates.
 */
public class Track : INotifyPropertyChanged
{
    private string _title = string.Empty;
    private string _artist = string.Empty;
    private bool _isFavorite;
    private int _playCount;
    private DateTime? _lastPlayed;
    private string? _albumArtPath;
    private string? _notes;
    private int _skipCount;
    private DateTime? _lastSkipped;
    private TimeSpan _duration = TimeSpan.Zero;

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    [PrimaryKey]
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
    public DateTime DateAdded { get; set; } = DateTime.UtcNow;

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

    public int SkipCount
    {
        get => _skipCount;
        set { if (_skipCount != value) { _skipCount = value; OnPropertyChanged(); } }
    }

    public DateTime? LastSkipped
    {
        get => _lastSkipped;
        set { if (_lastSkipped != value) { _lastSkipped = value; OnPropertyChanged(); } }
    }

    public List<string> Tags { get; set; } = new();

    public string? Album { get; set; }
    public int TrackNumber { get; set; } = 0;

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

    public TimeSpan Duration
    {
        get => _duration;
        set
        {
            if (_duration != value)
            {
                _duration = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayDuration));
            }
        }
    }

    // --- RADIO & AUDIOBOOK EXTENSIONS ---
    public long PlaybackPositionTicks { get; set; } = 0;
    public MediaType MediaType { get; set; } = MediaType.Music;
    public bool TitleForceCleaned { get; set; } = false;

    // --- COMPUTED PROPERTIES ---
    [Ignore] public bool IsRadio => MediaType == MediaType.Radio;
    [Ignore] public bool IsAudiobook => MediaType == MediaType.Audiobook;

    public string DisplayDuration => DurationFormatter.Format(_duration);

    public double PlaybackPositionPercent =>
        Duration.Ticks > 0 ? Math.Clamp(PlaybackPositionTicks * 100.0 / Duration.Ticks, 0, 100) : 0;

    public string ProgressLabel =>
        $"{PlaybackPositionPercent:0}% · {DurationFormatter.Format(TimeSpan.FromTicks(Math.Max(0, Duration.Ticks - PlaybackPositionTicks)))} left";

    public string DurationLabel => DurationFormatter.Format(Duration);

    /*
     * PERFORMANCE FIX: Removed `File.Exists(FilePath)` from here.
     * Checking the physical disk for 5,000 tracks simultaneously will freeze the UI thread.
     * We assume if `FilePath` is not null, it's "offline ready". The PlaybackService
     * will handle the actual File.Exists check gracefully when the user hits Play.
     */
    [Ignore]
    public bool IsOfflineReady => !string.IsNullOrEmpty(FilePath);
}

public enum TrackSource { YouTube, Spotify, SoundCloud, LastFm, Local, Unknown }
public enum MediaType { Music, Audiobook, Radio, Podcast }