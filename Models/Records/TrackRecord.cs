using System;
using System.Linq;
using NullWave.Helpers;
using SQLite;

namespace NullWave.Models;

[Table("Tracks")]
public class TrackRecord
{
    [PrimaryKey]
    public string Id { get; set; } = string.Empty;

    [Indexed]
    public string Title { get; set; } = string.Empty;

    [Indexed]
    public string Artist { get; set; } = string.Empty;

    public string? Url { get; set; }
    public string? FilePath { get; set; }
    public string Source { get; set; } = "Unknown";
    public DateTime DateAdded { get; set; } = DateTime.UtcNow;

    [Indexed]
    public bool IsFavorite { get; set; }

    public int PlayCount { get; set; }
    public DateTime? LastPlayed { get; set; }
    public string? AlbumArtPath { get; set; }
    public string? Notes { get; set; }
    public int SkipCount { get; set; }
    public DateTime? LastSkipped { get; set; }
    public string? TagsRaw { get; set; }
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
        FilePath = PathHelper.Tokenize(t.FilePath),
        Source = t.Source.ToString(),
        DateAdded = UtcTime.AsUtc(t.DateAdded),
        IsFavorite = t.IsFavorite,
        PlayCount = t.PlayCount,
        LastPlayed = UtcTime.AsUtc(t.LastPlayed),
        AlbumArtPath = PathHelper.Tokenize(t.AlbumArtPath),
        Notes = t.Notes,
        SkipCount = t.SkipCount,
        LastSkipped = UtcTime.AsUtc(t.LastSkipped),
        TagsRaw = t.Tags.Count > 0 ? string.Join("|", t.Tags.Select(x => x.Replace('|', '/'))) : null,
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
        FilePath = PathHelper.Resolve(FilePath),
        Source = Enum.TryParse<TrackSource>(Source, out var s) ? s : TrackSource.Unknown,
        DateAdded = UtcTime.AsUtc(DateAdded),
        IsFavorite = IsFavorite,
        PlayCount = PlayCount,
        LastPlayed = UtcTime.AsUtc(LastPlayed),
        AlbumArtPath = PathHelper.Resolve(AlbumArtPath),
        Notes = Notes,
        SkipCount = SkipCount,
        LastSkipped = UtcTime.AsUtc(LastSkipped),
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