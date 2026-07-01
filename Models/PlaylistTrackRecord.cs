using System;
using SQLite;

namespace NullWave.Models;

[Table("PlaylistTracks")]
public class PlaylistTrackRecord
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }
    
    [Indexed]
    public string PlaylistId { get; set; } = string.Empty;
    
    [Indexed] // Prevents full table scans when updating/deleting a track
    public string TrackId { get; set; } = string.Empty;
    
    public int SortOrder { get; set; }
}