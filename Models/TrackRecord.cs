using System;
using NullWave.Helpers;
using SQLite;

namespace NullWave.Models;

/*
 * DEV NOTE: This is the SQLite-net POCO. It mirrors `Track` but uses primitive types
 * (strings, longs) because SQLite doesn't natively understand Guids, TimeSpans, or Lists.
 * We use the FromTrack/ToTrack mappers to translate between the DB and the App.
 */
[Table("Tracks")]
public class TrackRecord
{
    [PrimaryKey]
    public string Id { get; set; } = string.Empty;

    [Indexed] // Speeds up search queries
    public string Title { get; set; } = string.Empty;

    [Indexed] // Speeds up artist grouping/filtering
    public string Artist { get; set; } = string.Empty;

    public string? Url { get; set; }
    public string? FilePath { get; set; }
    public string Source { get; set; } = "Unknown";
    public DateTime DateAdded { get; set; } = DateTime.UtcNow;

    [Indexed] // Speeds up the "Favorites" filter tab
    public bool IsFavorite { get; set; }

    public int PlayCount { get; set; }
    public DateTime? LastPlayed { get; set; }
    public string? AlbumArtPath { get; set; }
    public string? Notes { get; set; }
    public int SkipCount { get; set; }
    public DateTime? LastSkipped { get; set; }

    // Stored as a pipe-delimited string because SQLite doesn't support arrays natively
    public string? TagsRaw { get; set; }

    // Stored as milliseconds (long) for SQLite compatibility
    public long DurationMs { get; set; } = 0;
    public string MediaType { get; set; } = "Music";
    public long PlaybackPositionTicks { get; set; } = 0;
    public bool TitleForceCleaned { get; set; } = false;
    public string? Album { get; set; }
    public int TrackNumber { get; set; } = 0;

    public static TrackRecord FromTrack(Track t) => new()
    {
        Id = t.Id.ToString(),
        Title = t.Title,
        Artist = t.Artist,
        Url = t.Url,
        FilePath = PathHelper.Tokenize(t.FilePath),      // Portable paths!
        Source = t.Source.ToString(),
        DateAdded = t.DateAdded,
        IsFavorite = t.IsFavorite,
        PlayCount = t.PlayCount,
        LastPlayed = t.LastPlayed,
        AlbumArtPath = PathHelper.Tokenize(t.AlbumArtPath),
        Notes = t.Notes,
        SkipCount = t.SkipCount,
        LastSkipped = t.LastSkipped,
        TagsRaw = t.Tags.Count > 0 ? string.Join("|", t.Tags) : null,
        DurationMs = (long)t.Duration.TotalMilliseconds,
        MediaType = t.MediaType.ToString(),
        PlaybackPositionTicks = t.PlaybackPositionTicks,
        TitleForceCleaned = t.TitleForceCleaned,
        Album = t.Album,
        TrackNumber = t.TrackNumber,
    };

    public Track ToTrack() => new()
    {
        Id = Guid.TryParse(Id, out var g) ? g : Guid.NewGuid(),
        Title = Title,
        Artist = Artist,
        Url = Url,
        FilePath = PathHelper.Resolve(FilePath),         // Resolve back to OS paths
        Source = Enum.TryParse<TrackSource>(Source, out var s) ? s : TrackSource.Unknown,
        DateAdded = DateAdded,
        IsFavorite = IsFavorite,
        PlayCount = PlayCount,
        LastPlayed = LastPlayed,
        AlbumArtPath = PathHelper.Resolve(AlbumArtPath),
        Notes = Notes,
        SkipCount = SkipCount,
        LastSkipped = LastSkipped,
        // FIX: Added RemoveEmptyEntries to prevent ghost empty tags from malformed DB strings
        Tags = string.IsNullOrEmpty(TagsRaw)
            ? new()
            : new(TagsRaw.Split('|', StringSplitOptions.RemoveEmptyEntries)),
        Duration = TimeSpan.FromMilliseconds(DurationMs),
        MediaType = Enum.TryParse<global::NullWave.Models.MediaType>(MediaType, out var mt) ? mt : global::NullWave.Models.MediaType.Music,
        PlaybackPositionTicks = PlaybackPositionTicks,
        TitleForceCleaned = TitleForceCleaned,
        Album = Album,
        TrackNumber = TrackNumber,
    };
}
