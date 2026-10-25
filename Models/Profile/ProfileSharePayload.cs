using System.Collections.Generic;

namespace NullWave.Models;

public sealed record ProfileSharePayload
{
    public int V { get; init; } = 1;
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public string Bio { get; init; } = "";
    public List<string> TopArtists { get; init; } = new();
    public List<string> TopTags { get; init; } = new();
    public List<SharedTrack> TopTracks { get; init; } = new();
    public List<string> Badges { get; init; } = new();
    public int Tracks { get; init; }
    public int Favorites { get; init; }
    public int Hours { get; init; }
    public string Accent { get; init; } = "#F2A33C";

    public sealed record SharedTrack(string Title, string Artist);
}