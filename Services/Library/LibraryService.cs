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

/// <summary>
/// A track whose stored title doesn't match the title embedded in its linked audio
/// file's tags — a strong signal the FilePath points at the wrong file.
/// </summary>
public record LinkMismatch(
    Guid TrackId,
    string StoredTitle,
    string StoredArtist,
    string EmbeddedTitle,
    string EmbeddedArtist,
    string FilePath);

public record DuplicateGroup(string Title, string Artist, List<Track> Tracks);

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
                    Log.Debug("[LibraryService] YouTube thumbnail backfilled for {Title}", track.Title);
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
                        Log.Debug("[LibraryService] SoundCloud backfilled: {Title}", track.Title);
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
            Log.Debug("[LibraryService] Removed track with bad URL: {Url}", track.Url);
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

    /// <summary>
    /// Re-extracts embedded album art from a track's current FilePath and persists
    /// it immediately. Used after RelinkFile so a relinked track gets its correct
    /// artwork right away instead of waiting for the next startup's BackfillAlbumArt
    /// pass (which would otherwise leave the thumbnail blank until app restart).
    /// </summary>
    public void RefreshAlbumArt(Track track)
    {
        if (_metadata == null || string.IsNullOrEmpty(track.FilePath)) return;

        track.AlbumArtPath = _metadata.ExtractAlbumArt(track.FilePath);
        _db.Update(track);
        StateVersion++;
        OnLibraryChanged();
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
            Log.Debug("[LibraryService] Dead file path: {Path} (track: {Title})",
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

    /// <summary>
    /// Re-links downloaded audio files to library tracks whose FilePath is missing or
    /// dead. Two-pass matching, safest signal first:
    ///
    ///   Pass 1 — exact match: filename (no extension) equals the track title,
    ///   case-insensitive. The only fully trustworthy signal, so it runs first and
    ///   can't be overridden by a looser match below.
    ///
    ///   Pass 2 — token match: both strings are split into alphanumeric words (words
    ///   of length ≤2 dropped as noise — "ft", "hd", etc). A track only matches a file
    ///   if every one of its title's tokens appears as a whole word in the filename.
    ///   Titles with fewer than 2 significant tokens are skipped entirely in this pass,
    ///   since a single short word ("Low", "Down", "Scream") can appear inside unrelated
    ///   filenames (slowed, windows, screamer) — that was the actual bug that mis-linked
    ///   several tracks to the wrong audio in production.
    ///
    /// Within one call, a track can be matched at most once and a file can be claimed
    /// by at most one track (matchedTrackIds/matchedFiles), which also fixes a second
    /// bug: the same track being re-linked twice in a single run, with the second match
    /// silently overwriting the first.
    ///
    /// Only tracks with a missing or already-dead FilePath are eligible, so a track
    /// that's already correctly linked can never have its file stolen by a looser match
    /// found later in the scan.
    /// </summary>
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

        var candidates = _tracks
            .Where(t => string.IsNullOrEmpty(t.FilePath) || !File.Exists(t.FilePath))
            .ToList();

        var matchedTrackIds = new HashSet<Guid>();
        var matchedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var toUpdate = new List<Track>();

        // Pass 1: exact title == filename match.
        foreach (var file in files)
        {
            var fileNameNoExt = Path.GetFileNameWithoutExtension(file);
            var exact = candidates.FirstOrDefault(t =>
                !matchedTrackIds.Contains(t.Id) &&
                string.Equals(t.Title, fileNameNoExt, StringComparison.OrdinalIgnoreCase));

            if (exact == null) continue;

            exact.FilePath = file;
            matchedTrackIds.Add(exact.Id);
            matchedFiles.Add(file);
            toUpdate.Add(exact);
            Log.Debug("[LibraryService] Re-linked (exact) '{Title}' → {File}", exact.Title, file);
        }

        // Pass 2: word-boundary token match for remaining files/tracks.
        foreach (var file in files)
        {
            if (matchedFiles.Contains(file)) continue;

            var fileTokens = Tokenize(Path.GetFileNameWithoutExtension(file));
            if (fileTokens.Count == 0) continue;

            var match = candidates.FirstOrDefault(t =>
            {
                if (matchedTrackIds.Contains(t.Id)) return false;
                var titleTokens = Tokenize(t.Title);
                return titleTokens.Count >= 2 && titleTokens.IsSubsetOf(fileTokens);
            });

            if (match == null) continue;

            match.FilePath = file;
            matchedTrackIds.Add(match.Id);
            matchedFiles.Add(file);
            toUpdate.Add(match);
            Log.Debug("[LibraryService] Re-linked (token match) '{Title}' → {File}", match.Title, file);
        }

        foreach (var track in toUpdate)
            _db.Update(track);

        if (toUpdate.Count > 0) StateVersion++;
        Log.Information("[LibraryService] ReimportAssets: {Files} scanned, {Relinked} re-linked",
            files.Count, toUpdate.Count);

        return toUpdate.Count;
    }

    private static HashSet<string> Tokenize(string s) =>
        System.Text.RegularExpressions.Regex
            .Matches(s.ToLowerInvariant(), @"[a-z0-9]+")
            .Select(m => m.Value)
            .Where(w => w.Length > 2)
            .ToHashSet();

    /// <summary>
    /// Cross-checks every track with an existing FilePath against that file's embedded
    /// tags, catching links that point at the wrong audio — the kind of damage the old
    /// ReimportAssets substring bug caused, which file-existence checks like RepairPaths
    /// can never detect since the file at the wrong path genuinely exists.
    ///
    /// A mismatch is flagged when neither the stored title nor the embedded tag title
    /// (both normalised: lowercased, punctuation stripped) contains the other. Tracks
    /// whose file has no readable title tag are skipped rather than flagged — an absent
    /// tag isn't evidence of a wrong link, just an untagged file.
    /// </summary>
    public (int Checked, List<LinkMismatch> Mismatches) VerifyLinks()
    {
        var mismatches = new List<LinkMismatch>();

        if (_metadata == null)
        {
            Log.Warning("[LibraryService] VerifyLinks: no MetadataService available, cannot read embedded tags");
            return (0, mismatches);
        }

        var withFile = _tracks
            .Where(t => !string.IsNullOrEmpty(t.FilePath) && File.Exists(t.FilePath))
            .ToList();

        foreach (var track in withFile)
        {
            (string Title, string Artist) embedded;
            try
            {
                embedded = _metadata.FetchFromLocalFile(track.FilePath!);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[LibraryService] VerifyLinks: couldn't read tags for {Path}", track.FilePath);
                continue;
            }

            if (string.IsNullOrWhiteSpace(embedded.Title)) continue;

            if (TitlesLooselyMatch(track.Title, embedded.Title)) continue;

            var mismatch = new LinkMismatch(
                track.Id, track.Title, track.Artist,
                embedded.Title, embedded.Artist, track.FilePath!);
            mismatches.Add(mismatch);

            Log.Warning(
                "[LibraryService] Possible mis-link: stored '{StoredTitle}' by '{StoredArtist}' " +
                "→ file tagged '{EmbeddedTitle}' by '{EmbeddedArtist}' ({Path})",
                mismatch.StoredTitle, mismatch.StoredArtist,
                mismatch.EmbeddedTitle, mismatch.EmbeddedArtist, mismatch.FilePath);
        }

        Log.Information("[LibraryService] VerifyLinks: {Checked} tracks checked, {Mismatches} possible mismatch(es) found",
            withFile.Count, mismatches.Count);

        return (withFile.Count, mismatches);
    }

    private static bool TitlesLooselyMatch(string storedTitle, string embeddedTitle)
    {
        var a = NormalizeForCompare(storedTitle);
        var b = NormalizeForCompare(embeddedTitle);
        if (a.Length == 0 || b.Length == 0) return true; // nothing usable to compare, don't flag
        return a.Contains(b) || b.Contains(a);
    }

    private static string NormalizeForCompare(string s)
    {
        var decomposed = (s ?? string.Empty).Normalize(System.Text.NormalizationForm.FormD);
        var stripped = new string(decomposed
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                        != System.Globalization.UnicodeCategory.NonSpacingMark)
            .ToArray());
        return new string(stripped.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
    }

    /// <summary>
    /// Retroactively re-parses every track's Title for an embedded "Artist - Title"
    /// pattern and, where found, overwrites both Title and Artist with the parsed
    /// values — unconditionally, even if Artist already has a value. This exists
    /// because YouTube uploads from compilation/repost channels (e.g. a "Minecraft
    /// Volume Alpha" playlist uploaded by a channel called "SMORT") store the channel
    /// name as Artist, which is wrong but not "empty," so the normal title-splitting
    /// fallback used elsewhere in the app (which only fires when Artist is missing or
    /// "Unknown") never corrects it.
    ///
    /// This is a best-effort heuristic, not a guarantee: titles with more than one
    /// " - " segment (e.g. "Bitter Sweet - Kanye West - Def Poetry") can parse into
    /// the wrong artist/title split, since there's no reliable way to know which
    /// segment is the real artist — spot-check results afterward, especially on
    /// multi-dash titles.
    /// </summary>
    public int ForceCleanTitles()
    {
        int cleaned = 0;
        foreach (var track in _tracks)
        {
            if (track.TitleForceCleaned) continue; // never re-split a track twice

            var parsed = Metadata.TrackTitleParser.TryParseArtistTitle(track.Title);
            if (parsed == null) 
            { 
                track.TitleForceCleaned = true; 
                _db.Update(track); 
                continue; 
            }

            var (parsedArtist, parsedTitle) = parsed.Value;
            if (string.IsNullOrWhiteSpace(parsedArtist) || string.IsNullOrWhiteSpace(parsedTitle))
            {
                track.TitleForceCleaned = true;
                _db.Update(track);
                continue;
            }

            if (parsedArtist == track.Artist && parsedTitle == track.Title)
            {
                track.TitleForceCleaned = true;
                _db.Update(track);
                continue;
            }

            Log.Debug("[LibraryService] Force-cleaned: '{OldTitle}' by '{OldArtist}' → '{NewTitle}' by '{NewArtist}'",
                track.Title, track.Artist, parsedTitle, parsedArtist);

            track.Title = parsedTitle;
            track.Artist = parsedArtist;
            track.TitleForceCleaned = true; // mark done regardless of outcome
            _db.Update(track);
            cleaned++;
        }

        if (cleaned > 0) StateVersion++;
        Log.Information("[LibraryService] ForceCleanTitles: {Count} of {Total} tracks cleaned",
            cleaned, _tracks.Count);
        return cleaned;
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
                Log.Debug("[LibraryService] Swept orphaned file: {Path}", fullPath);
            }
            catch (Exception ex)
            {
                failed++;
                Log.Warning(ex, "[LibraryService] Failed to delete orphaned file: {Path}", fullPath);
            }
        }

        Log.Information("[LibraryService] SweepOrphanedFiles: {Scanned} scanned, {Orphaned} orphaned, {Deleted} deleted, {Failed} failed (dryRun={DryRun})",
            audioFiles.Count, orphaned, deleted, failed, dryRun);

        return (audioFiles.Count, orphaned, deleted, failed);
    }

    /// <summary>
    /// Finds tracks sharing the same normalized (Title, Artist) — a real duplicate
    /// signal now that ForceCleanTitles/ImportViewModel's sanitizer normalize titles
    /// on both import and force-clean. Within each duplicate group, keeps one "best"
    /// track and (if not dryRun) deletes the rest. Keeper priority: has a FilePath
    /// that exists on disk > higher PlayCount > IsFavorite > earliest DateAdded.
    /// </summary>
    public (int Scanned, int DuplicateGroups, int Removed) RemoveDuplicates(bool dryRun = true)
    {
        var groups = _tracks
            .GroupBy(t => (Title: t.Title.Trim().ToLowerInvariant(), Artist: t.Artist.Trim().ToLowerInvariant()))
            .Where(g => g.Count() > 1)
            .ToList();

        int removed = 0;
        foreach (var group in groups)
        {
            var ordered = group
                .OrderByDescending(t => !string.IsNullOrEmpty(t.FilePath) && File.Exists(t.FilePath))
                .ThenByDescending(t => t.PlayCount)
                .ThenByDescending(t => t.IsFavorite)
                .ThenBy(t => t.DateAdded)
                .ToList();

            var keeper = ordered[0];
            var duplicates = ordered.Skip(1).ToList();

            foreach (var dup in duplicates)
            {
                Log.Information("[LibraryService] Duplicate: keeping '{KeepTitle}' ({KeepPath}), removing '{DupTitle}' ({DupPath})",
                    keeper.Title, keeper.FilePath ?? keeper.Url, dup.Title, dup.FilePath ?? dup.Url);

                if (!dryRun)
                {
                    _tracks.Remove(dup);
                    _db.Delete(dup.Id);
                    removed++;
                }
            }
        }

        if (removed > 0) StateVersion++;
        Log.Information("[LibraryService] RemoveDuplicates: {Scanned} tracks scanned, {Groups} duplicate group(s) found, {Removed} removed (dryRun={DryRun})",
            _tracks.Count, groups.Count, removed, dryRun);

        return (_tracks.Count, groups.Count, removed);
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