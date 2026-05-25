using System;
using System.Collections.Generic;
using System.Linq;
using NullWave.Models;

namespace NullWave.Services;

public class LibraryService
{
    private readonly List<Track> _tracks = new();

    public IReadOnlyList<Track> GetAll() => _tracks.AsReadOnly();

    public void Add(Track track) => _tracks.Add(track);

    public void Remove(Guid id)
    {
        var track = _tracks.FirstOrDefault(t => t.Id == id);
        if (track != null) _tracks.Remove(track);
    }

    public IReadOnlyList<Track> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return GetAll();

        return _tracks
            .Where(t => t.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                     || t.Artist.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public IReadOnlyList<Track> FilterBySource(TrackSource source) =>
        _tracks.Where(t => t.Source == source).ToList();
}