using System;

namespace NullWave.Services;

/// <summary>Reserved, never-persisted GUIDs that represent virtual smart playlists in the sidebar.</summary>
public static class SmartPlaylistIds
{
    public static Guid ToGuid(string defId) => defId switch
    {
        "smart.recently-added"      => Guid.Parse("00000000-0000-0000-0000-000000000001"),
        "smart.most-played"         => Guid.Parse("00000000-0000-0000-0000-000000000002"),
        "smart.forgotten-favorites" => Guid.Parse("00000000-0000-0000-0000-000000000003"),
        _ => Guid.Empty
    };

    public static string? ToDefId(Guid id) => id.ToString() switch
    {
        "00000000-0000-0000-0000-000000000001" => "smart.recently-added",
        "00000000-0000-0000-0000-000000000002" => "smart.most-played",
        "00000000-0000-0000-0000-000000000003" => "smart.forgotten-favorites",
        _ => null
    };
}