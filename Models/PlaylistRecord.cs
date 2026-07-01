using System;
using SQLite;

namespace NullWave.Models;

[Table("Playlists")]
public class PlaylistRecord
{
    [PrimaryKey]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public static PlaylistRecord FromPlaylist(Playlist p) => new()
    {
        Id = p.Id.ToString(),
        Name = p.Name,
        Description = p.Description,
        CreatedAt = p.DateCreated
    };

    public Playlist ToPlaylist() => new()
    {
        Id = Guid.TryParse(Id, out var g) ? g : Guid.NewGuid(),
        Name = Name,
        Description = Description,
        DateCreated = CreatedAt
    };
}