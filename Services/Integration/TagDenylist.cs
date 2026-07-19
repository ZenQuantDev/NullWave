using System;
using System.Collections.Generic;

namespace NullWave.Services.Integration;

/// <summary>
/// Single source of truth for Last.fm tags that should never be applied to a
/// track, regardless of which code path fetched them. Previously LastFmService
/// and LastFmEnrichmentService each had their own separate filter, meaning a
/// tag blocked by one could still slip through the other.
///
/// Two categories:
///   - Generic: noise tags that add no genre/mood value ("seen live", etc.)
///   - Offensive: crowdsourced troll/hate tags that occasionally get voted onto
///     legitimate tracks. Add specific terms you've observed here — kept as a
///     plain list so it's easy to extend without touching filtering logic.
/// </summary>
public static class TagDenylist
{
    public static readonly HashSet<string> Generic = new(StringComparer.OrdinalIgnoreCase)
    {
        "seen live", "favorite", "favourites", "awesome", "amazing",
        "focus", "tdci", "klima", "szyby", "swoje", "fave", "track", "album", "song"
    };

    // Add specific troll/offensive tags you've observed reaching tracks below.
    // Case-insensitive, exact match against the tag string.
    public static readonly HashSet<string> Offensive = new(StringComparer.OrdinalIgnoreCase)
    {
        // e.g. "some troll tag", "another one"
        "heil hitler"
    };

    public static bool IsBlocked(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return true;
        return Generic.Contains(tag) || Offensive.Contains(tag);
    }
}