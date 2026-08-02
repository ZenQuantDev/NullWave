using System;
using SQLite;

namespace NullWave.Models;

[Table("PlaylistFolders")]
public class PlaylistFolderRecord
{
    [PrimaryKey]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
