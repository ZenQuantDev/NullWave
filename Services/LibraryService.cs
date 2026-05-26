using System;
using System.Collections.Generic;
using System.Linq;
using NullWave.Models;
using Serilog;

namespace NullWave.Services;

public class LibraryService
{
    private readonly List<Track> _tracks = new();
    private readonly List<Track> _queue = new();
    private readonly List<Track> _history = new();

    // ── Core ──────────────────────────────────────────
    public IReadOnlyList<Track> GetAll() => _tracks.AsReadOnly();

    public void Add(Track track)
    {
        if (IsDuplicate(track)) return;
        _tracks.Add(track);
    }

    public void Remove(Guid id)
    {
        var track = _tracks.FirstOrDefault(t => t.Id == id);
        if (track != null) _tracks.Remove(track);
    }

    // ── Search & Filter ───────────────────────────────
    public IReadOnlyList<Track> Search(string query, SortField field = SortField.DateAdded, bool ascending = true)
    {
        if (string.IsNullOrWhiteSpace(query)) return GetSorted(field, ascending);

        var results = _tracks
            .Where(t => t.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                     || t.Artist.Contains(query, StringComparison.OrdinalIgnoreCase));

        IEnumerable<Track> sorted = field switch
        {
            SortField.Title => results.OrderBy(t => t.Title),
            SortField.Artist => results.OrderBy(t => t.Artist),
            SortField.DateAdded => results.OrderBy(t => t.DateAdded),
            SortField.Source => results.OrderBy(t => t.Source),
            SortField.PlayCount => results.OrderBy(t => t.PlayCount),
            SortField.LastPlayed => results.OrderBy(t => t.LastPlayed),
            _ => results
        };

        return (ascending ? sorted : sorted.Reverse()).ToList();
    }

    public IReadOnlyList<Track> FilterBySource(TrackSource source) =>
        _tracks.Where(t => t.Source == source).ToList();

    public IReadOnlyList<Track> GetFavorites() =>
        _tracks.Where(t => t.IsFavorite).ToList();

    public IReadOnlyList<Track> GetRecentlyAdded(int count = 20) =>
        _tracks.OrderByDescending(t => t.DateAdded).Take(count).ToList();

    public IReadOnlyList<Track> GetRecentlyPlayed(int count = 20) =>
        _history.TakeLast(count).Reverse().ToList();

    // ── Sorting ───────────────────────────────────────
    public IReadOnlyList<Track> GetSorted(SortField field, bool ascending = true)
    {
        IEnumerable<Track> sorted = field switch
        {
            SortField.Title => _tracks.OrderBy(t => t.Title),
            SortField.Artist => _tracks.OrderBy(t => t.Artist),
            SortField.DateAdded => _tracks.OrderBy(t => t.DateAdded),
            SortField.Source => _tracks.OrderBy(t => t.Source),
            SortField.PlayCount => _tracks.OrderBy(t => t.PlayCount),
            SortField.LastPlayed => _tracks.OrderBy(t => t.LastPlayed),
            _ => _tracks
        };

        return (ascending ? sorted : sorted.Reverse()).ToList();
    }

    // ── Favorites ─────────────────────────────────────
    public void ToggleFavorite(Guid id)
    {
        var track = _tracks.FirstOrDefault(t => t.Id == id);
        if (track != null) track.IsFavorite = !track.IsFavorite;
    }

    // ── Play Tracking ─────────────────────────────────
    public void RecordPlay(Guid id)
    {
        var track = _tracks.FirstOrDefault(t => t.Id == id);
        if (track == null) return;

        track.PlayCount++;
        track.LastPlayed = DateTime.Now;
        _history.Add(track);

        if (_history.Count > 200)
            _history.RemoveAt(0);
    }

    // ── Duplicate Detection ───────────────────────────
    public bool IsDuplicate(Track newTrack)
    {
        return _tracks.Any(t =>
            (!string.IsNullOrWhiteSpace(newTrack.Url) && t.Url == newTrack.Url) ||
            (!string.IsNullOrWhiteSpace(newTrack.FilePath) && t.FilePath == newTrack.FilePath) ||
            (!string.IsNullOrWhiteSpace(newTrack.Title) &&
             !string.IsNullOrWhiteSpace(newTrack.Artist) &&
             t.Title.Equals(newTrack.Title, StringComparison.OrdinalIgnoreCase) &&
             t.Artist.Equals(newTrack.Artist, StringComparison.OrdinalIgnoreCase)));
    }

    // ── Queue ─────────────────────────────────────────
    public IReadOnlyList<Track> GetQueue() => _queue.AsReadOnly();

    public void AddToQueue(Guid id)
    {
        var track = _tracks.FirstOrDefault(t => t.Id == id);
        if (track != null && !_queue.Contains(track))
            _queue.Add(track);
    }

    public void RemoveFromQueue(Guid id)
    {
        var track = _queue.FirstOrDefault(t => t.Id == id);
        if (track != null) _queue.Remove(track);
    }

    public void ClearQueue() => _queue.Clear();

    public Track? DequeueNext()
    {
        if (_queue.Count == 0) return null;
        var next = _queue[0];
        _queue.RemoveAt(0);
        return next;
    }
}

public enum SortField
{
    Title,
    Artist,
    DateAdded,
    Source,
    PlayCount,
    LastPlayed
}