// LibraryService.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NullWave.Helpers;
using NullWave.Models;
using SQLite;
using Serilog;

namespace NullWave.Services;

public class LibraryService : IDisposable
{
    private readonly DatabaseService _db;
    private readonly MetadataService? _metadata;
    private List<Track> _tracks;
    private readonly object _tracksLock = new();
    private readonly List<Track> _queue = new();
    private readonly List<Track> _history = new();

    public event EventHandler? LibraryChanged;
    public int StateVersion { get; private set; } = 0;

    public LibraryService(DatabaseService db, MetadataService? metadata = null)
    {
        _db = db;
        _metadata = metadata;
        _tracks = _db.LoadAll();
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
            var updatedTracks = new List<Track>();
            foreach (var track in ytTracks)
            {
                try
                {
                    var id = Metadata.YouTubeMetadataFetcher.ExtractYouTubeId(track.Url!);
                    if (string.IsNullOrEmpty(id)) continue;
                    var thumbPath = await Metadata.YouTubeMetadataFetcher.FetchThumbnailAsync(id);
                    if (string.IsNullOrEmpty(thumbPath)) continue;
                    track.AlbumArtPath = thumbPath;
                    updatedTracks.Add(track);
                    Log.Information("[LibraryService] YouTube thumbnail backfilled for {Title}", track.Title);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "[LibraryService] YouTube thumbnail backfill failed for {Title}", track.Title);
                }
            }

            if (updatedTracks.Count > 0)
            {
                _db.RunInTransaction(() =>
                {
                    foreach (var track in updatedTracks)
                    {
                        _db.Update(track);
                    }
                });
                StateVersion++;
                Avalonia.Threading.Dispatcher.UIThread.Post(() => LibraryChanged?.Invoke(this, EventArgs.Empty));
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

        Log.Information("[LibraryService] Backfilling thumbnails for {Count} SoundCloud tracks", scTracks.Count);
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            var fetcher = new Metadata.SoundCloudMetadataFetcher();
            var updatedTracks = new List<Track>();
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
                        updatedTracks.Add(track);
                        Log.Information("[LibraryService] SoundCloud backfilled: {Title}", track.Title);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "[LibraryService] SoundCloud backfill failed for {Title}", track.Title);
                }
            }

            if (updatedTracks.Count > 0)
            {
                _db.RunInTransaction(() =>
                {
                    foreach (var track in updatedTracks)
                    {
                        _db.Update(track);
                    }
                });
                StateVersion++;
                Avalonia.Threading.Dispatcher.UIThread.Post(() => LibraryChanged?.Invoke(this, EventArgs.Empty));
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

        if (bad.Count == 0) return;

        foreach (var track in bad)
        {
            _tracks.Remove(track);
            _db.Delete(track.Id);
            Log.Warning("[LibraryService] Removed track with bad URL: {Url}", track.Url);
        }

        StateVersion++;
        Log.Information("[LibraryService] Cleaned {Count} bad tracks from DB", bad.Count);
    }

    private void BackfillAlbumArt()
    {
        if (_metadata == null) return;

        var updatedTracks = new List<Track>();
        foreach (var track in _tracks)
        {
            if (!string.IsNullOrEmpty(track.AlbumArtPath)) continue;
            if (string.IsNullOrEmpty(track.FilePath)) continue;
            if (!System.IO.File.Exists(track.FilePath)) continue;

            var art = _metadata.ExtractAlbumArt(track.FilePath);
            if (art == null) continue;

            track.AlbumArtPath = art;
            updatedTracks.Add(track);
        }

        if (updatedTracks.Count > 0)
        {
            _db.RunInTransaction(() =>
            {
                foreach (var track in updatedTracks)
                {
                    _db.Update(track);
                }
            });
            StateVersion++;
            Avalonia.Threading.Dispatcher.UIThread.Post(() => LibraryChanged?.Invoke(this, EventArgs.Empty));
            Log.Information("[LibraryService] Album art backfill complete");
        }
    }

    public IReadOnlyList<Track> GetAll() => _tracks.AsReadOnly();

    public void Add(Track track)
    {
        if (IsDuplicate(track)) return;

        if (!string.IsNullOrEmpty(track.FilePath) &&
            string.IsNullOrEmpty(track.AlbumArtPath) &&
            _metadata != null)
        {
            track.AlbumArtPath = _metadata.ExtractAlbumArt(track.FilePath);
        }

        _tracks.Add(track);
        _db.Insert(track);
        StateVersion++;
        OnLibraryChanged();
    }

    public void Remove(Guid id)
    {
        var track = _tracks.FirstOrDefault(t => t.Id == id);
        if (track == null) return;

        _tracks.Remove(track);
        _db.Delete(id);
        StateVersion++;
        OnLibraryChanged();
    }

    public void Update(Track track)
    {
        _db.Update(track);
        var idx = _tracks.FindIndex(t => t.Id == track.Id);
        if (idx >= 0) _tracks[idx] = track;
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

    public void ToggleFavorite(Guid id)
    {
        var track = _tracks.FirstOrDefault(t => t.Id == id);
        if (track == null) return;

        track.IsFavorite = !track.IsFavorite;
        _db.Update(track);
        StateVersion++;
    }

    public void RecordPlay(Guid id)
    {
        var track = _tracks.FirstOrDefault(t => t.Id == id);
        if (track == null) return;

        track.PlayCount++;
        track.LastPlayed = DateTime.Now;
        _db.Update(track);
        StateVersion++;

        _history.Add(track);
        if (_history.Count > 200)
            _history.RemoveAt(0);
    }

    public bool IsDuplicate(Track newTrack)
    {
        return _tracks.Any(t =>
            (!string.IsNullOrWhiteSpace(newTrack.Url) &&
             string.Equals(t.Url, newTrack.Url, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(newTrack.FilePath) &&
             string.Equals(t.FilePath, newTrack.FilePath, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(newTrack.Title) &&
             !string.IsNullOrWhiteSpace(newTrack.Artist) &&
             string.Equals(t.Title, newTrack.Title, StringComparison.OrdinalIgnoreCase) &&
             string.Equals(t.Artist, newTrack.Artist, StringComparison.OrdinalIgnoreCase)));
    }

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

    public int ClearAllArt()
    {
        int cleared = 0;
        foreach (var track in _tracks)
        {
            if (string.IsNullOrEmpty(track.AlbumArtPath)) continue;
            track.AlbumArtPath = null;
            _db.Update(track);
            cleared++;
        }

        if (cleared > 0) StateVersion++;

        try
        {
            if (Directory.Exists(NullWavePaths.ArtCacheDir))
            {
                foreach (var file in Directory.EnumerateFiles(NullWavePaths.ArtCacheDir))
                {
                    try { File.Delete(file); }
                    catch (Exception ex) { Log.Warning(ex, "[LibraryService] Could not delete art file: {File}", file); }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[LibraryService] Failed to clear art cache directory");
        }

        Log.Information("[LibraryService] Cleared album art for {Count} tracks and wiped art cache", cleared);
        return cleared;
    }

    public void RebackfillThumbnails()
    {
        BackfillYouTubeThumbnails();
        BackfillSoundCloudThumbnails();
    }

    public (int total, int missing, int removed) RepairPaths(bool removeDeadEntries = false)
    {
        var withPath = _tracks.Where(t => !string.IsNullOrEmpty(t.FilePath)).ToList();
        int missing = 0;
        int removed = 0;

        foreach (var track in withPath)
        {
            if (File.Exists(track.FilePath)) continue;
            missing++;
            Log.Warning("[LibraryService] Dead file path: {Path} (track: {Title})",
                track.FilePath, track.Title);

            if (!removeDeadEntries) continue;

            track.FilePath = null;
            _db.Update(track);
            removed++;
        }

        if (removed > 0) StateVersion++;
        Log.Information("[LibraryService] RepairPaths: {Total} checked, {Missing} missing, {Removed} cleared",
            withPath.Count, missing, removed);

        return (withPath.Count, missing, removed);
    }

    public int ReimportAssets(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            Log.Warning("[LibraryService] ReimportAssets: directory not found: {Path}", directoryPath);
            return 0;
        }

        var audioExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".mp3", ".flac", ".m4a", ".ogg", ".wav", ".aac", ".opus" };

        var files = Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories)
            .Where(f => audioExtensions.Contains(Path.GetExtension(f)))
            .ToList();

        int relinked = 0;
        foreach (var file in files)
        {
            var fileNameNoExt = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
            var match = _tracks.FirstOrDefault(t =>
                !string.Equals(t.FilePath, file, StringComparison.OrdinalIgnoreCase) &&
                (fileNameNoExt.Contains(t.Title.ToLowerInvariant(), StringComparison.Ordinal) ||
                 t.Title.ToLowerInvariant().Contains(fileNameNoExt, StringComparison.Ordinal) ||
                 (t.FilePath != null &&
                  Path.GetFileNameWithoutExtension(t.FilePath)
                      .Equals(Path.GetFileNameWithoutExtension(file),
                          StringComparison.OrdinalIgnoreCase))));

            if (match == null) continue;

            Log.Information("[LibraryService] Re-linked '{Title}' → {File}", match.Title, file);
            match.FilePath = file;
            _db.Update(match);
            relinked++;
        }

        if (relinked > 0) StateVersion++;
        Log.Information("[LibraryService] ReimportAssets: {Files} scanned, {Relinked} re-linked",
            files.Count, relinked);

        return relinked;
    }

    public int ClearTagsForReSync()
    {
        int cleared = 0;
        foreach (var track in _tracks)
        {
            if (track.Tags.Count == 0) continue;
            track.Tags.Clear();
            _db.Update(track);
            cleared++;
        }

        if (cleared > 0) StateVersion++;
        Log.Information("[LibraryService] ClearTagsForReSync: cleared tags on {Count} tracks", cleared);
        return cleared;
    }

    /// <summary>
    /// Scans the downloads directory for audio files with no matching Track.FilePath
    /// in the database, and deletes them. Returns (scanned, orphaned, deletedOk, failedCount).
    /// </summary>
    public (int Scanned, int Orphaned, int Deleted, int Failed) SweepOrphanedFiles(string downloadsDir, bool dryRun = false)
    {
        if (!Directory.Exists(downloadsDir))
            return (0, 0, 0, 0);

        var knownPaths = new HashSet<string>(
            GetAll()
                .Where(t => !string.IsNullOrEmpty(t.FilePath))
                .Select(t => Path.GetFullPath(t.FilePath!)),
            StringComparer.OrdinalIgnoreCase);

        var audioFiles = Directory.GetFiles(downloadsDir, "*.*", SearchOption.TopDirectoryOnly)
            .Where(f => f.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase))
            .ToList();

        int orphaned = 0, deleted = 0, failed = 0;

        foreach (var file in audioFiles)
        {
            var fullPath = Path.GetFullPath(file);
            if (knownPaths.Contains(fullPath)) continue;

            orphaned++;
            if (dryRun) continue;

            try
            {
                File.Delete(fullPath);
                deleted++;
                Log.Information("[LibraryService] Swept orphaned file: {Path}", fullPath);
            }
            catch (Exception ex)
            {
                failed++;
                Log.Warning(ex, "[LibraryService] Failed to delete orphaned file: {Path}", fullPath);
            }
        }

        return (audioFiles.Count, orphaned, deleted, failed);
    }

    public (long BeforeKB, long AfterKB) VacuumDatabase()
    {
        var before = new FileInfo(_db.DbPath).Length / 1024;
        _db.Vacuum();
        var after = new FileInfo(_db.DbPath).Length / 1024;
        return (before, after);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    private void OnLibraryChanged()
    {
        LibraryChanged?.Invoke(this, EventArgs.Empty);
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