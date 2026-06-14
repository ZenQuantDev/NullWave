using System;
using System.Collections.Generic;
using System.Linq;
using NullWave.Helpers;
using NullWave.Models;
using Serilog;

namespace NullWave.Services;

public class LibraryService
{
    private readonly DatabaseService _db;
    private readonly MetadataService? _metadata;
    private List<Track> _tracks;
    private readonly List<Track> _queue   = new();
    private readonly List<Track> _history = new();

    public LibraryService(MetadataService? metadata = null)
    {
        _db       = new DatabaseService();
        _metadata = metadata;
        _tracks   = _db.LoadAll();
        Log.Information("[LibraryService] Loaded {Count} tracks from DB", _tracks.Count);
        CleanupBadUrls();
        BackfillAlbumArt();
        BackfillYouTubeThumbnails();
        BackfillSoundCloudThumbnails();
    }
    private void BackfillYouTubeThumbnails()
    {
        var ytTracks = _tracks
            .Where(t => t.Source == TrackSource.YouTube
                     && string.IsNullOrEmpty(t.AlbumArtPath)
                     && !string.IsNullOrEmpty(t.Url))
            .ToList();

        if (ytTracks.Count == 0) return;

        Log.Information("[LibraryService] Backfilling thumbnails for {Count} YouTube tracks", ytTracks.Count);

        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            foreach (var track in ytTracks)
            {
                try
                {
                    var id = Metadata.YouTubeMetadataFetcher.ExtractYouTubeId(track.Url!);
                    if (string.IsNullOrEmpty(id)) continue;

                    var thumbPath = await Metadata.YouTubeMetadataFetcher.FetchThumbnailAsync(id);
                    if (string.IsNullOrEmpty(thumbPath)) continue;

                    track.AlbumArtPath = thumbPath;
                    _db.Update(track);
                    Log.Information("[LibraryService] YouTube thumbnail backfilled for {Title}", track.Title);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "[LibraryService] YouTube thumbnail backfill failed for {Title}", track.Title);
                }
            }
        });
    }

    private void BackfillSoundCloudThumbnails()
    {
        var scTracks = _tracks
            .Where(t => t.Source == TrackSource.SoundCloud
                     && string.IsNullOrEmpty(t.AlbumArtPath)
                     && !string.IsNullOrEmpty(t.Url))
            .ToList();

        if (scTracks.Count == 0) return;

        Log.Information("[LibraryService] Backfilling thumbnails for {Count} SoundCloud tracks",
            scTracks.Count);

        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            var fetcher = new Metadata.SoundCloudMetadataFetcher();
            foreach (var track in scTracks)
            {
                try
                {
                    var (title, artist, thumbPath) = await fetcher.FetchAsync(track.Url!);

                    bool changed = false;
                    if (!string.IsNullOrEmpty(thumbPath) && string.IsNullOrEmpty(track.AlbumArtPath))
                    {
                        track.AlbumArtPath = thumbPath;
                        changed = true;
                    }
                    if ((track.Title == track.Url || track.Title == "SoundCloud track"
                         || string.IsNullOrWhiteSpace(track.Title))
                        && !string.IsNullOrWhiteSpace(title))
                    {
                        track.Title = title;
                        changed = true;
                    }
                    if ((track.Artist == "Unknown" || string.IsNullOrWhiteSpace(track.Artist))
                        && !string.IsNullOrWhiteSpace(artist))
                    {
                        track.Artist = artist;
                        changed = true;
                    }

                    if (changed)
                    {
                        _db.Update(track);
                        Log.Information("[LibraryService] SoundCloud backfilled: {Title}", track.Title);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "[LibraryService] SoundCloud backfill failed for {Title}",
                        track.Title);
                }
            }
        });
    }

    private void CleanupBadUrls()
    {
        var bad = _tracks
            .Where(t => !string.IsNullOrEmpty(t.Url)
                     && !SourceDetector.IsPlayableUrl(t.Url)
                     && string.IsNullOrEmpty(t.FilePath))
            .ToList();

        foreach (var track in bad)
        {
            _tracks.Remove(track);
            _db.Delete(track.Id);
            Log.Warning("[LibraryService] Removed track with bad URL: {Url}", track.Url);
        }

        if (bad.Count > 0)
            Log.Information("[LibraryService] Cleaned {Count} bad tracks from DB", bad.Count);
    }

    private void BackfillAlbumArt()
    {
        if (_metadata == null) return;
        bool anyUpdated = false;
        foreach (var track in _tracks)
        {
            if (!string.IsNullOrEmpty(track.AlbumArtPath)) continue;
            if (string.IsNullOrEmpty(track.FilePath)) continue;
            if (!System.IO.File.Exists(track.FilePath)) continue;

            var art = _metadata.ExtractAlbumArt(track.FilePath);
            if (art == null) continue;

            track.AlbumArtPath = art;
            _db.Update(track);
            anyUpdated = true;
        }
        if (anyUpdated)
            Log.Information("[LibraryService] Album art backfill complete");
    }

    // ── Core ──────────────────────────────────────────
    public IReadOnlyList<Track> GetAll() => _tracks.AsReadOnly();

    public void Add(Track track)
    {
        if (IsDuplicate(track)) return;

        // Extract embedded album art for local files
        if (!string.IsNullOrEmpty(track.FilePath) &&
            string.IsNullOrEmpty(track.AlbumArtPath) &&
            _metadata != null)
        {
            track.AlbumArtPath = _metadata.ExtractAlbumArt(track.FilePath);
        }

        _tracks.Add(track);
        _db.Insert(track);
    }

    public void Remove(Guid id)
    {
        var track = _tracks.FirstOrDefault(t => t.Id == id);
        if (track == null) return;
        _tracks.Remove(track);
        _db.Delete(id);
    }

    public void Update(Track track)
    {
        _db.Update(track);
        // Ensure the in-memory reference is the same object
        var idx = _tracks.FindIndex(t => t.Id == track.Id);
        if (idx >= 0) _tracks[idx] = track;
    }

    // ── Search & Filter ───────────────────────────────
    public IReadOnlyList<Track> Search(
        string query, SortField field = SortField.DateAdded, bool ascending = true)
    {
        if (string.IsNullOrWhiteSpace(query)) return GetSorted(field, ascending);

        var results = _tracks
            .Where(t => t.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                     || t.Artist.Contains(query, StringComparison.OrdinalIgnoreCase));

        IEnumerable<Track> sorted = field switch
        {
            SortField.Title      => results.OrderBy(t => t.Title),
            SortField.Artist     => results.OrderBy(t => t.Artist),
            SortField.DateAdded  => results.OrderBy(t => t.DateAdded),
            SortField.Source     => results.OrderBy(t => t.Source),
            SortField.PlayCount  => results.OrderBy(t => t.PlayCount),
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
            SortField.Title      => _tracks.OrderBy(t => t.Title),
            SortField.Artist     => _tracks.OrderBy(t => t.Artist),
            SortField.DateAdded  => _tracks.OrderBy(t => t.DateAdded),
            SortField.Source     => _tracks.OrderBy(t => t.Source),
            SortField.PlayCount  => _tracks.OrderBy(t => t.PlayCount),
            SortField.LastPlayed => _tracks.OrderBy(t => t.LastPlayed),
            _ => _tracks
        };

        return (ascending ? sorted : sorted.Reverse()).ToList();
    }

    // ── Favorites ─────────────────────────────────────
    public void ToggleFavorite(Guid id)
    {
        var track = _tracks.FirstOrDefault(t => t.Id == id);
        if (track == null) return;
        track.IsFavorite = !track.IsFavorite;
        _db.Update(track);
    }

    // ── Play Tracking ─────────────────────────────────
    public void RecordPlay(Guid id)
    {
        var track = _tracks.FirstOrDefault(t => t.Id == id);
        if (track == null) return;

        track.PlayCount++;
        track.LastPlayed = DateTime.Now;
        _db.Update(track);

        _history.Add(track);
        if (_history.Count > 200)
            _history.RemoveAt(0);
    }

    // ── Duplicate Detection ───────────────────────────
    public bool IsDuplicate(Track newTrack)
    {
        return _tracks.Any(t =>
            (!string.IsNullOrWhiteSpace(newTrack.Url)      && t.Url      == newTrack.Url) ||
            (!string.IsNullOrWhiteSpace(newTrack.FilePath) && t.FilePath == newTrack.FilePath) ||
            (!string.IsNullOrWhiteSpace(newTrack.Title) &&
             !string.IsNullOrWhiteSpace(newTrack.Artist) &&
             t.Title.Equals(newTrack.Title,   StringComparison.OrdinalIgnoreCase) &&
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