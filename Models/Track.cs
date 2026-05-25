using System;

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
}

public enum TrackSource
{
    YouTube,
    Spotify,
    Local,
    Instagram,
    Unknown
}