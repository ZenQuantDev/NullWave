using System;
using System.Collections.Generic;
using System.Linq;
using NullWave.Models;

namespace NullWave.Services;

public record SmartPlaylistDefinition(
    string Id,
    string NameKey,
    Func<IReadOnlyList<Track>, IEnumerable<Track>> Filter,
    Func<Track, object?> PrimarySort,
    Func<Track, object?> SecondarySort,
    bool SecondaryDescending,
    int Limit
);

public class SmartPlaylistService
{
    private readonly LibraryService _library;
    private readonly TimeProvider _clock;

    public SmartPlaylistService(LibraryService library, TimeProvider clock)
    {
        _library = library;
        _clock = clock;
    }

    public static readonly SmartPlaylistDefinition RecentlyAdded = new(
        Id: "smart.recently-added",
        NameKey: "SmartPlaylist_RecentlyAdded",
        Filter: tracks => tracks.Where(t => t.MediaType != MediaType.Radio && t.MediaType != MediaType.Audiobook),
        PrimarySort: t => t.DateAdded,
        SecondarySort: t => t.Title,
        SecondaryDescending: false,
        Limit: 100
    );

    public static readonly SmartPlaylistDefinition MostPlayed = new(
        Id: "smart.most-played",
        NameKey: "SmartPlaylist_MostPlayed",
        Filter: tracks => tracks.Where(t => t.PlayCount >= 1 && t.MediaType != MediaType.Radio && t.MediaType != MediaType.Audiobook),
        PrimarySort: t => t.PlayCount,
        SecondarySort: t => t.LastPlayed,
        SecondaryDescending: true,
        Limit: 100
    );

    public SmartPlaylistDefinition ForgottenFavorites => new(
        Id: "smart.forgotten-favorites",
        NameKey: "SmartPlaylist_ForgottenFavorites",
        Filter: tracks =>
        {
            var cutoff = _clock.GetUtcNow().AddDays(-90).UtcDateTime;
            return tracks.Where(t => 
                t.PlayCount >= 2 && 
                t.LastPlayed.HasValue && 
                t.LastPlayed.Value < cutoff &&
                t.MediaType != MediaType.Radio && 
                t.MediaType != MediaType.Audiobook);
        },
        PrimarySort: t => t.PlayCount,
        SecondarySort: t => t.LastPlayed,
        SecondaryDescending: false,
        Limit: 100
    );

    public IReadOnlyList<SmartPlaylistDefinition> All => new[] { RecentlyAdded, MostPlayed, ForgottenFavorites };

    public IReadOnlyList<Track> Resolve(SmartPlaylistDefinition definition)
    {
        var all = _library.GetAll();
        return ResolveInternal(definition, all);
    }

    // Exposed for unit testing without needing a real database
    internal IReadOnlyList<Track> ResolveInternal(SmartPlaylistDefinition definition, IEnumerable<Track> library)
    {
        var filtered = definition.Filter(library.ToList());
        
        return definition.SecondaryDescending
            ? filtered.OrderByDescending(definition.PrimarySort)
                      .ThenByDescending(definition.SecondarySort)
                      .Take(definition.Limit)
                      .ToList()
            : filtered.OrderByDescending(definition.PrimarySort)
                      .ThenBy(definition.SecondarySort)
                      .Take(definition.Limit)
                      .ToList();
    }
}