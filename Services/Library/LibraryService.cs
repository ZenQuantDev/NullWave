using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Threading;
using NullWave.Helpers;
using NullWave.Models;
using NullWave.Services.Metadata;
using Serilog;

namespace NullWave.Services;

public record LinkMismatch(Guid TrackId, string StoredTitle, string StoredArtist, string EmbeddedTitle, string EmbeddedArtist, string FilePath);
public record DuplicateGroup(string Title, string Artist, List<Track> Tracks);
public record ArtistMergeGroup(string CanonicalName, List<string> Variants, int TotalTracks);
public record FileSyncReport(int Scanned, int Retagged, int Renamed, int Skipped, int Failed);

public enum SortField { Title, Artist, DateAdded, Source, PlayCount, LastPlayed }

public partial class LibraryService : IDisposable
{
    private readonly DatabaseService _db;
    private readonly MetadataService? _metadata;
    private readonly PreferencesService? _prefs;
    private List<Track> _tracks;
    private readonly object _tracksLock = new();
    private readonly List<QueueEntry> _queue = new();
    private readonly List<Track> _history = new();

    public event EventHandler? LibraryChanged;
    public event EventHandler? QueueChanged;
    public int StateVersion { get; private set; } = 0;

    // FIX (C8): Added startBackgroundWork parameter so tests can instantiate the service
    // without triggering background TagLib I/O and network calls.
    public LibraryService(DatabaseService db, MetadataService? metadata = null, PreferencesService? prefs = null, bool startBackgroundWork = true)
    {
        _db = db;
        _metadata = metadata;
        _prefs = prefs;
        lock (_tracksLock) { _tracks = _db.LoadAll(); }
        Log.Information("[LibraryService] Loaded {Count} tracks from DB", _tracks.Count);

        if (!startBackgroundWork) return;

        _ = Task.Run(CleanupBadUrls);
        _ = Task.Run(BackfillAlbumArt);
        _ = Task.Run(BackfillYouTubeThumbnails);
        _ = Task.Run(BackfillSoundCloudThumbnails);
    }

    public IReadOnlyList<Track> GetAll()
    {
        lock (_tracksLock) return _tracks.ToList().AsReadOnly();
    }

    public void Add(Track track)
    {
        if (IsDuplicate(track)) return;

        if (!string.IsNullOrEmpty(track.FilePath) && string.IsNullOrEmpty(track.AlbumArtPath) && _metadata != null)
            track.AlbumArtPath = _metadata.ExtractAlbumArt(track.FilePath);

        if (_prefs?.Current.AutoCleanMetadata == true)
        {
            var titleToParse = CleanYouTubeArtifacts(track.Title, isArtist: false);
            var parsed = TrackTitleParser.TryParseArtistTitle(titleToParse);
            if (parsed != null && !string.IsNullOrWhiteSpace(parsed.Value.Artist) && !string.IsNullOrWhiteSpace(parsed.Value.Title))
            {
                if (track.Artist == "Unknown" || track.Artist == "Unknown Artist" || string.IsNullOrWhiteSpace(track.Artist))
                    track.Artist = parsed.Value.Artist;
                if (track.Title == track.Url || track.Title == "Unknown Title" || string.IsNullOrWhiteSpace(track.Title))
                    track.Title = parsed.Value.Title;
            }
        }

        lock (_tracksLock) _tracks.Add(track);
        _db.Insert(track);
        StateVersion++;
        OnLibraryChanged();
        UpdateFileTags(track);
    }

    public void Remove(Guid id)
    {
        Track? track;
        lock (_tracksLock) track = _tracks.FirstOrDefault(t => t.Id == id);
        if (track == null) return;
        lock (_tracksLock) _tracks.Remove(track);
        _db.Delete(id);
        StateVersion++;
        OnLibraryChanged();
    }

    public void Update(Track track)
    {
        Task.Run(() => {
            try { _db.Update(track); }
            catch (Exception ex) { Log.Error(ex, "DB Update failed for {Title}", track.Title); }
        });

        lock (_tracksLock)
        {
            var idx = _tracks.FindIndex(t => t.Id == track.Id);
            if (idx >= 0) _tracks[idx] = track;
        }
        StateVersion++;
        OnLibraryChanged();
    }

    public Task UpdateTrackMetadataAsync(Track track)
    {
        return Task.Run(() =>
        {
            _db.Update(track);
            lock (_tracksLock)
            {
                var idx = _tracks.FindIndex(t => t.Id == track.Id);
                if (idx >= 0) _tracks[idx] = track;
                StateVersion++;
            }
        });
    }

    public IReadOnlyList<Track> Search(string query, SortField field = SortField.DateAdded, bool ascending = true)
    {
        if (string.IsNullOrWhiteSpace(query)) return GetSorted(field, ascending);
        List<Track> snapshot;
        lock (_tracksLock) snapshot = _tracks.ToList();
        var results = snapshot.Where(t => t.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || t.Artist.Contains(query, StringComparison.OrdinalIgnoreCase));
        IEnumerable<Track> sorted = field switch
        {
            SortField.Title => results.OrderBy(t => t.Title), SortField.Artist => results.OrderBy(t => t.Artist),
            SortField.DateAdded => results.OrderBy(t => t.DateAdded), SortField.Source => results.OrderBy(t => t.Source),
            SortField.PlayCount => results.OrderBy(t => t.PlayCount), SortField.LastPlayed => results.OrderBy(t => t.LastPlayed),
            _ => results
        };
        return (ascending ? sorted : sorted.Reverse()).ToList();
    }

    public IReadOnlyList<Track> FilterBySource(TrackSource source) => GetAll().Where(t => t.Source == source).ToList();
    public IReadOnlyList<Track> GetFavorites() => GetAll().Where(t => t.IsFavorite).ToList();
    public IReadOnlyList<Track> GetRecentlyAdded(int count = 20) => GetAll().OrderByDescending(t => t.DateAdded).Take(count).ToList();
    public IReadOnlyList<Track> GetRecentlyPlayed(int count = 20) => _history.TakeLast(count).Reverse().ToList();

    public IReadOnlyList<Track> GetSorted(SortField field, bool ascending = true)
    {
        var snapshot = GetAll().ToList();
        IEnumerable<Track> sorted = field switch
        {
            SortField.Title => snapshot.OrderBy(t => t.Title), SortField.Artist => snapshot.OrderBy(t => t.Artist),
            SortField.DateAdded => snapshot.OrderBy(t => t.DateAdded), SortField.Source => snapshot.OrderBy(t => t.Source),
            SortField.PlayCount => snapshot.OrderBy(t => t.PlayCount), SortField.LastPlayed => snapshot.OrderBy(t => t.LastPlayed),
            _ => snapshot
        };
        return (ascending ? sorted : sorted.Reverse()).ToList();
    }

    public void ToggleFavorite(Guid id)
    {
        Track? track;
        lock (_tracksLock) track = _tracks.FirstOrDefault(t => t.Id == id);
        if (track == null) return;
        track.IsFavorite = !track.IsFavorite;
        _db.Update(track);
        StateVersion++;
    }

    public void RecordPlay(Guid id)
    {
        Track? track;
        lock (_tracksLock) track = _tracks.FirstOrDefault(t => t.Id == id);
        if (track == null) return;
        track.PlayCount++;
        track.LastPlayed = DateTime.Now;
        _db.Update(track);
        StateVersion++;
        _history.Add(track);
        if (_history.Count > 200) _history.RemoveAt(0);
        OnLibraryChanged();
    }

    public bool IsDuplicate(Track newTrack)
    {
        lock (_tracksLock) return _tracks.Any(t =>
            (!string.IsNullOrWhiteSpace(newTrack.Url) && string.Equals(t.Url, newTrack.Url, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(newTrack.FilePath) && string.Equals(t.FilePath, newTrack.FilePath, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(newTrack.Title) && !string.IsNullOrWhiteSpace(newTrack.Artist) &&
             string.Equals(t.Title, newTrack.Title, StringComparison.OrdinalIgnoreCase) && string.Equals(t.Artist, newTrack.Artist, StringComparison.OrdinalIgnoreCase)));
    }

    // --- Queue Methods ---
    public IReadOnlyList<QueueEntry> GetQueue() => _queue.AsReadOnly();
    public void AddToQueue(Guid id)
    {
        Track? track;
        lock (_tracksLock) track = _tracks.FirstOrDefault(t => t.Id == id);
        if (track != null && !_queue.Any(e => e.Track.Id == track.Id))
        {
            int insertIndex = _prefs?.Current.QueueManualInsertAtBlockEnd == true ? _queue.FindIndex(e => !e.IsManual) : 0;
            if (insertIndex < 0) insertIndex = _queue.Count;
            _queue.Insert(insertIndex, new QueueEntry(track, IsManual: true));
            QueueChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public void RemoveFromQueue(Guid id) { var e = _queue.FirstOrDefault(x => x.Track.Id == id); if (e != null) { _queue.Remove(e); QueueChanged?.Invoke(this, EventArgs.Empty); } }
    public void RestoreQueue(IEnumerable<QueueEntry> entries) { if (_queue.Count > 0) return; _queue.AddRange(entries); QueueChanged?.Invoke(this, EventArgs.Empty); }
    public void ClearQueue() { if (_queue.Count == 0) return; _queue.Clear(); QueueChanged?.Invoke(this, EventArgs.Empty); }
    public void ClearAutoQueue() { foreach (var e in _queue.Where(e => !e.IsManual).ToList()) _queue.Remove(e); QueueChanged?.Invoke(this, EventArgs.Empty); }
    public Track? DequeueNext() { if (_queue.Count == 0) return null; var n = _queue[0]; _queue.RemoveAt(0); QueueChanged?.Invoke(this, EventArgs.Empty); return n.Track; }
    public void FillQueue(IEnumerable<Track> autoTracks)
    {
        var target = _prefs?.Current.QueueAutoFillSize ?? 20;
        var needed = target - _queue.Count(e => !e.IsManual);
        if (needed <= 0) return;
        foreach (var t in autoTracks.Take(needed)) _queue.Add(new QueueEntry(t, IsManual: false));
        QueueChanged?.Invoke(this, EventArgs.Empty);
    }
    public bool MoveQueueItem(int from, int to)
    {
        if (from < 0 || to < 0 || from >= _queue.Count || to >= _queue.Count) return false;
        var e = _queue[from]; _queue.RemoveAt(from); _queue.Insert(to, e);
        QueueChanged?.Invoke(this, EventArgs.Empty); return true;
    }

    public void Dispose() => _db.Dispose();
    private void OnLibraryChanged() => LibraryChanged?.Invoke(this, EventArgs.Empty);
}