using System;
using NullWave.Helpers;
using SQLite;

namespace NullWave.Models;

[Table("Playlists")]
public class PlaylistRecord
{
    [PrimaryKey]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? FolderId { get; set; }
    public string? CustomArtPath { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime DateAdded { get; set; }

    public static PlaylistRecord FromPlaylist(Playlist p) => new()
    {
        Id = p.Id.ToString(),
        Name = p.Name,
        Description = p.Description,
        FolderId = p.FolderId?.ToString(),
        CustomArtPath = PathHelper.Tokenize(p.CustomArtPath),     // <--- TOKENIZE
        CreatedAt = p.DateCreated
    };

    public Playlist ToPlaylist() => new()
    {
        Id = Guid.TryParse(Id, out var g) ? g : Guid.NewGuid(),
        Name = Name,
        Description = Description,
        FolderId = string.IsNullOrWhiteSpace(FolderId) ? null : Guid.TryParse(FolderId, out var folderId) ? folderId : null,
        CustomArtPath = PathHelper.Resolve(CustomArtPath),        // <--- RESOLVE
        DateCreated = CreatedAt
    };
}