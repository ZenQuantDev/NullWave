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
}

[Table("PlaylistTracks")]
public class PlaylistTrackRecord
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }
    
    [Indexed]
    public string PlaylistId { get; set; } = "";
    
    public string TrackId { get; set; } = "";
    public int SortOrder { get; set; }
}