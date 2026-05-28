using System;
using System.Collections.Generic;

namespace NullWave.Models;

public class Track
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string? Url { get; set; }
    public string? FilePath { get; set; }
    public TrackSource Source { get; set; }
    public DateTime DateAdded { get; set; } = DateTime.Now;

    // Favorites
    public bool IsFavorite { get; set; } = false;

    // Play tracking
    public int PlayCount { get; set; } = 0;
    public DateTime? LastPlayed { get; set; }

    // Tags
    public List<string> Tags { get; set; } = new();

    // Notes
    public string? Notes { get; set; }
}

public enum TrackSource
{
    YouTube,
    Spotify,
    SoundCloud,
    LastFm,
    Local,
    Unknown
}