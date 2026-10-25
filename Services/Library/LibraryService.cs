using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
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

// REMOVED: public enum SortField { ... } (Now lives in Models/Library/SortField.cs)

public partial class LibraryService : IDisposable
{
    private readonly DatabaseService _db;
    private readonly MetadataService? _metadata;
    private readonly PreferencesService? _prefs;
    private List<Track> _tracks;
    private readonly object _tracksLock = new();
    private readonly List<QueueEntry> _queue = new();
    private readonly List<Track> _history = new();

    // --- Step A: O(1) Duplicate Detection Index ---
    private readonly Dictionary<string, Guid> _identityIndex = new();
    private int _identityIndexVersion = -1;

    private System.Threading.Timer? _changeTimer;
    private int _changePending;

    public event EventHandler? LibraryChanged;
    public event EventHandler<Guid>? TrackRemovedFromLibrary;
    public void BackupDatabase(string path) => _db.BackupToPath(path);
    public event EventHandler? QueueChanged;
    public int StateVersion { get; private set; } = 0;

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
        
        _ = Task.Run(async () =>
        {
            await Task.Delay(1500); 

            List<Track> targets;
            lock (_tracksLock)
            {
                targets = _tracks.Where(t => !string.IsNullOrEmpty(t.AlbumArtPath) && File.Exists(t.AlbumArtPath)).ToList();
            }
            if (targets.Count == 0) return;

            var warmed = new List<Avalonia.Media.IImage>(targets.Count);
            int i = 0;
            foreach (var t in targets)
            {
                try
                {
                    var img = BitmapCacheService.DecodeSync(t.AlbumArtPath!, 96);
                    if (img != null) warmed.Add(img);
                }
                catch { }
                if (++i % 40 == 0) await Task.Delay(8);
            }
            Log.Information("[LibraryService] Decoded {Count}/{Total} track art bitmaps", warmed.Count, targets.Count);

            BitmapPrewarm.PrewarmGpuTextures(warmed, 24, 96);
        });
    }

    // --- Index Helpers ---
    private void EnsureIdentityIndex()
    {
        if (_identityIndexVersion == StateVersion) return;
        _identityIndex.Clear();
        foreach (var t in _tracks)
            foreach (var k in GetIdentityKeys(t)) _identityIndex.TryAdd(k, t.Id);
        _identityIndexVersion = StateVersion;
    }

    private static IEnumerable<string> GetIdentityKeys(Track t)
    {
        if (!string.IsNullOrWhiteSpace(t.Url)) yield return $"url:{t.Url.ToLowerInvariant()}";
        if (!string.IsNullOrWhiteSpace(t.FilePath)) yield return $"path:{t.FilePath.ToLowerInvariant()}";
        
        var normTitle = TrackNormalizer.NormalizeForCompare(t.Title);
        var normArtist = TrackNormalizer.NormalizeForCompare(t.Artist);
        if (normTitle.Length > 0 && normArtist.Length > 0) 
            yield return $"ta:{normArtist}|{normTitle}";
    }

    public IReadOnlyList<Track> GetAll()
    {
        lock (_tracksLock) return _tracks.ToList().AsReadOnly();
    }

    public void Add(Track track)
    {
        if (!string.IsNullOrEmpty(track.FilePath))
        {
            var importedPath = track.FilePath;
            if (importedPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(segment => string.Equals(segment, "..", StringComparison.Ordinal)))
            {
                Log.Warning("[LibraryService] Blocked invalid or traversal file path: {Path}", importedPath);
                return;
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(importedPath);
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
            {
                Log.Warning("[LibraryService] Blocked invalid or traversal file path: {Path}", importedPath);
                return;
            }

            if (!File.Exists(fullPath))
            {
                Log.Warning("[LibraryService] Blocked invalid or traversal file path: {Path}", importedPath);
                return;
            }

            track.FilePath = fullPath;
        }

        // Step A: O(1) Duplicate Check & Index Update
        lock (_tracksLock) 
        {
            EnsureIdentityIndex();
            if (GetIdentityKeys(track).Any(k => _identityIndex.TryGetValue(k, out var existingId) && existingId != track.Id)) 
                return; // Duplicate found, abort add

            _tracks.Add(track);
            foreach (var k in GetIdentityKeys(track)) _identityIndex.TryAdd(k, track.Id);
            StateVersion++;
            _identityIndexVersion = StateVersion;
        }
        
        _db.Insert(track);
        OnLibraryChanged();
        UpdateFileTags(track);

        if (!string.IsNullOrEmpty(track.FilePath) && string.IsNullOrEmpty(track.AlbumArtPath) && _metadata != null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var artPath = await _metadata.ExtractAlbumArtAsync(track.FilePath);
                    if (!string.IsNullOrEmpty(artPath))
                    {
                        track.AlbumArtPath = artPath;
                        Update(track); 
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Background album art extraction failed for {Path}", track.FilePath);
                }
            });
        }

        if (_prefs?.Current.AutoCleanMetadata == true)
        {
            var split = TrackNormalizer.TrySplit(track.Title);
            if (split != null && !string.IsNullOrWhiteSpace(split.Value.Artist) && !string.IsNullOrWhiteSpace(split.Value.Title))
            {
                if (TrackNormalizer.IsPlaceholderArtist(track.Artist))
                    track.Artist = split.Value.Artist;
                if (TrackNormalizer.IsPlaceholderTitle(track.Title))
                    track.Title = split.Value.Title;
                
                Update(track); // Persist cleaned metadata
            }
        }
    }

    public void Remove(Guid id)
    {
        Track? track;
        lock (_tracksLock) 
        {
            track = _tracks.FirstOrDefault(t => t.Id == id);
            if (track == null) return;
            
            _tracks.Remove(track);
            // Clean index
            foreach (var k in GetIdentityKeys(track)) _identityIndex.Remove(k);
            StateVersion++;
            _identityIndexVersion = StateVersion;
        }
        
        var queueEntry = _queue.FirstOrDefault(e => e.Track.Id == id);
        if (queueEntry != null) { _queue.Remove(queueEntry); QueueChanged?.Invoke(this, EventArgs.Empty); }
        
        _db.Delete(id);
        OnLibraryChanged();
        TrackRemovedFromLibrary?.Invoke(this, id);
    }

    public void Update(Track track)
    {
        _db.UpdateAsync(track);

        lock (_tracksLock)
        {
            var idx = _tracks.FindIndex(t => t.Id == track.Id);
            if (idx >= 0) _tracks[idx] = track;
            
            // Invalidate index so it rebuilds on next check (safest for metadata edits)
            StateVersion++;
            _identityIndexVersion = -1; 
        }
        OnLibraryChanged();
    }

    public Task UpdateTrackMetadataAsync(Track track)
    {
        return Task.Run(() =>
        {
            _db.UpdateAsync(track);
            lock (_tracksLock)
            {
                var idx = _tracks.FindIndex(t => t.Id == track.Id);
                if (idx >= 0) _tracks[idx] = track;
                StateVersion++;
                _identityIndexVersion = -1;
            }
        });
    }

    // --- Step B: Unified Sort Logic ---
    private static IReadOnlyList<Track> SortTracks(IEnumerable<Track> source, SortField field, bool ascending)
    {
        if (field == SortField.Length)
        {
            var known = source.Where(t => t.Duration > TimeSpan.Zero).ToList();
            var zero = source.Where(t => t.Duration <= TimeSpan.Zero);
            var ordered = ascending ? known.OrderBy(t => t.Duration) : known.OrderByDescending(t => t.Duration);
            return ordered.ThenBy(t => t.Title, StringComparer.OrdinalIgnoreCase).Concat(zero).ToList();
        }

        Func<Track, object?> key = field switch
        {
            SortField.Title => t => t.Title,
            SortField.Artist => t => t.Artist,
            SortField.Source => t => t.Source,
            SortField.PlayCount => t => t.PlayCount,
            SortField.LastPlayed => t => t.LastPlayed,
            _ => t => t.DateAdded // DateAdded, Custom fallback
        };
        
        var sorted = ascending ? source.OrderBy(key) : source.OrderByDescending(key);
        return sorted.ThenBy(t => t.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public IReadOnlyList<Track> Search(string query, SortField field = SortField.DateAdded, bool ascending = true)
    {
        if (string.IsNullOrWhiteSpace(query)) return GetSorted(field, ascending);
        List<Track> snapshot;
        lock (_tracksLock) snapshot = _tracks.ToList();
        var results = snapshot.Where(t => t.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || t.Artist.Contains(query, StringComparison.OrdinalIgnoreCase));
        return SortTracks(results, field, ascending);
    }

    public HashSet<Guid>? SearchFts(string query) => _db.SearchFts(query);

    public void RebuildFtsIndex() => _db.RebuildFtsIndex();

    public IReadOnlyList<Track> FilterBySource(TrackSource source) => GetAll().Where(t => t.Source == source).ToList();
    public IReadOnlyList<Track> GetFavorites() => GetAll().Where(t => t.IsFavorite).ToList();
    public IReadOnlyList<Track> GetRecentlyAdded(int count = 20) => GetAll().OrderByDescending(t => t.DateAdded).Take(count).ToList();
    public IReadOnlyList<Track> GetRecentlyPlayed(int count = 20) => _history.TakeLast(count).Reverse().ToList();

    public IReadOnlyList<Track> GetSorted(SortField field, bool ascending = true)
    {
        var snapshot = GetAll();
        return SortTracks(snapshot, field, ascending);
    }

    public void ToggleFavorite(Guid id)
    {
        Track? track;
        lock (_tracksLock) track = _tracks.FirstOrDefault(t => t.Id == id);
        if (track == null) return;
        track.IsFavorite = !track.IsFavorite;
        _db.UpdateAsync(track);
        StateVersion++;
    }

    public void RecordPlay(Guid id)
    {
        Track? track;
        lock (_tracksLock) track = _tracks.FirstOrDefault(t => t.Id == id);
        if (track == null) return;
        track.PlayCount++;
        track.LastPlayed = DateTime.UtcNow; 
        _db.UpdateAsync(track);
        StateVersion++;
        _history.Add(track);
        if (_history.Count > 200) _history.RemoveAt(0);
        OnLibraryChanged();
    }

    public bool IsDuplicate(Track newTrack)
    {
        lock (_tracksLock)
        {
            EnsureIdentityIndex();
            return GetIdentityKeys(newTrack).Any(k => _identityIndex.TryGetValue(k, out var id) && id != newTrack.Id);
        }
    }

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

    private void OnLibraryChanged()
    {
        if (Interlocked.Exchange(ref _changePending, 1) == 1) return;
        _changeTimer ??= new System.Threading.Timer(_ =>
        {
            Interlocked.Exchange(ref _changePending, 0);
            Dispatcher.UIThread.Post(() => LibraryChanged?.Invoke(this, EventArgs.Empty));
        });
        _changeTimer.Change(250, Timeout.Infinite);
    }

    public void Dispose() 
    { 
        _changeTimer?.Dispose(); 
        _db.Dispose(); 
    }
}