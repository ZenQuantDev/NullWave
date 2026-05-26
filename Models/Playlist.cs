using System;
using System.Collections.Generic;

namespace NullWave.Models;

public class Playlist
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime DateCreated { get; set; } = DateTime.Now;
    public List<Track> Tracks { get; set; } = new();
}