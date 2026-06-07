using System;
using SQLite;

namespace NullWave.Models;

/// <summary>
/// Flat SQLite-mapped record. Converted to/from Track for all in-memory work.
/// </summary>
[Table("Tracks")]
public class TrackRecord
{
    [PrimaryKey]
    public string Id { get; set; } = string.Empty;

    public string Title      { get; set; } = string.Empty;
    public string Artist     { get; set; } = string.Empty;
    public string? Url       { get; set; }
    public string? FilePath  { get; set; }
    public string Source     { get; set; } = "Unknown";
    public DateTime DateAdded { get; set; } = DateTime.Now;
    public bool IsFavorite   { get; set; }
    public int PlayCount     { get; set; }
    public DateTime? LastPlayed { get; set; }
    public string? AlbumArtPath { get; set; }
    public string? Notes     { get; set; }

    // Tags stored as pipe-separated string: "rock|indie|chill"
    public string? TagsRaw   { get; set; }

    // ── Conversion helpers ────────────────────────────────────────────────

    public static TrackRecord FromTrack(Track t) => new()
    {
        Id          = t.Id.ToString(),
        Title       = t.Title,
        Artist      = t.Artist,
        Url         = t.Url,
        FilePath    = t.FilePath,
        Source      = t.Source.ToString(),
        DateAdded   = t.DateAdded,
        IsFavorite  = t.IsFavorite,
        PlayCount   = t.PlayCount,
        LastPlayed  = t.LastPlayed,
        AlbumArtPath = t.AlbumArtPath,
        Notes       = t.Notes,
        TagsRaw     = t.Tags.Count > 0 ? string.Join("|", t.Tags) : null
    };

    public Track ToTrack() => new()
    {
        Id          = Guid.TryParse(Id, out var g) ? g : Guid.NewGuid(),
        Title       = Title,
        Artist      = Artist,
        Url         = Url,
        FilePath    = FilePath,
        Source      = Enum.TryParse<TrackSource>(Source, out var s) ? s : TrackSource.Unknown,
        DateAdded   = DateAdded,
        IsFavorite  = IsFavorite,
        PlayCount   = PlayCount,
        LastPlayed  = LastPlayed,
        AlbumArtPath = AlbumArtPath,
        Notes       = Notes,
        Tags        = string.IsNullOrEmpty(TagsRaw)
                        ? new()
                        : new(TagsRaw.Split('|', StringSplitOptions.RemoveEmptyEntries))
    };
}