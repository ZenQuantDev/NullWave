using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NullWave.Helpers;
using NullWave.Models;
using SQLite;
using Serilog;

namespace NullWave.Services;

public record LinkMismatch(
    Guid TrackId,
    string StoredTitle,
    string StoredArtist,
    string EmbeddedTitle,
    string EmbeddedArtist,
    string FilePath);

public record DuplicateGroup(string Title, string Artist, List<Track> Tracks);

public record ArtistMergeGroup(string CanonicalName, List<string> Variants, int TotalTracks);

public class LibraryService : IDisposable
{
    private readonly DatabaseService _db;
    private readonly MetadataService? _metadata;
    private readonly PreferencesService? _prefs;
    private List<Track> _tracks;
    private readonly object _tracksLock = new();
    private readonly List<QueueEntry> _queue = new();
    private readonly List<Track> _history = new();

    private static readonly Regex ArtistSeparatorRegex =
        new(@"\s*(?:,|&|\band\b|\bfeat\.?\b|\bft\.?\b|\bfeaturing\b)\s*",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public event EventHandler? LibraryChanged;
    public event EventHandler? QueueChanged;
    public int StateVersion { get; private set; } = 0;

    public LibraryService(DatabaseService db, MetadataService? metadata = null, PreferencesService? prefs = null)
    {
        _db = db;
        _metadata = metadata;
        _prefs = prefs;
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
        _ = Task.Run(async () =>
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
        _ = Task.Run(async () =>
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
            if (!File.Exists(track.FilePath)) continue;

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

    public IReadOnlyList<QueueEntry> GetQueue() => _queue.AsReadOnly();

    public void AddToQueue(Guid id)
    {
        var track = _tracks.FirstOrDefault(t => t.Id == id);
        if (track != null && !_queue.Any(e => e.Track.Id == track.Id))
        {
            int insertIndex = _prefs?.Current.QueueManualInsertAtBlockEnd == true
                ? _queue.FindIndex(e => !e.IsManual)
                : 0;
            
            if (insertIndex < 0) insertIndex = _queue.Count;
            
            _queue.Insert(insertIndex, new QueueEntry(track, IsManual: true));
            QueueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void RemoveFromQueue(Guid id)
    {
        var entry = _queue.FirstOrDefault(e => e.Track.Id == id);
        if (entry != null)
        {
            _queue.Remove(entry);
            QueueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void ClearQueue()
    {
        if (_queue.Count == 0) return;
        _queue.Clear();
        QueueChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearAutoQueue()
    {
        var autoEntries = _queue.Where(e => !e.IsManual).ToList();
        foreach (var entry in autoEntries)
            _queue.Remove(entry);
        QueueChanged?.Invoke(this, EventArgs.Empty);
    }

    public Track? DequeueNext()
    {
        if (_queue.Count == 0) return null;
        var next = _queue[0];
        _queue.RemoveAt(0);
        QueueChanged?.Invoke(this, EventArgs.Empty);
        return next.Track;
    }

    public void FillQueue(IEnumerable<Track> autoTracks)
    {
        var currentAutoCount = _queue.Count(e => !e.IsManual);
        var target = _prefs?.Current.QueueAutoFillSize ?? 20;
        
        if (currentAutoCount >= target) return;

        var needed = target - currentAutoCount;
        foreach (var track in autoTracks.Take(needed))
            _queue.Add(new QueueEntry(track, IsManual: false));
        
        QueueChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool MoveQueueItem(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || toIndex < 0) return false;
        if (fromIndex >= _queue.Count || toIndex >= _queue.Count) return false;

        var entry = _queue[fromIndex];
        _queue.RemoveAt(fromIndex);
        _queue.Insert(toIndex, entry);
        QueueChanged?.Invoke(this, EventArgs.Empty);
        return true;
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
        Regex
            .Matches(s.ToLowerInvariant(), @"[a-z0-9]+")
            .Select(m => m.Value)
            .Where(w => w.Length > 2)
            .ToHashSet();

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
            (string Title, string Artist, TimeSpan Duration) embedded;
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
        if (a.Length == 0 || b.Length == 0) return true;
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

    private bool HasVerifiedFile(Track t)
    {
        if (string.IsNullOrEmpty(t.FilePath) || !File.Exists(t.FilePath)) return false;
        if (_metadata == null) return true;

        try
        {
            var embedded = _metadata.FetchFromLocalFile(t.FilePath);
            if (string.IsNullOrWhiteSpace(embedded.Title)) return true;
            return TitlesLooselyMatch(t.Title, embedded.Title);
        }
        catch
        {
            return true;
        }
    }

    public List<ArtistMergeGroup> FindSimilarArtistGroups()
    {
        var groups = _tracks
            .Where(t => !string.IsNullOrWhiteSpace(t.Artist))
            .GroupBy(t => NormalizeArtistKey(t.Artist))
            .Where(g => g.Select(t => t.Artist).Distinct(StringComparer.Ordinal).Count() > 1)
            .Select(g =>
            {
                var variantCounts = g.GroupBy(t => t.Artist, StringComparer.Ordinal)
                    .Select(vg => (Name: vg.Key, Count: vg.Count()))
                    .OrderByDescending(v => v.Count)
                    .ThenBy(v => v.Name, StringComparer.Ordinal)
                    .ToList();

                return new ArtistMergeGroup(
                    CanonicalName: variantCounts[0].Name,
                    Variants: variantCounts.Select(v => v.Name).ToList(),
                    TotalTracks: g.Count());
            })
            .OrderByDescending(g => g.TotalTracks)
            .ToList();

        Log.Information("[LibraryService] FindSimilarArtistGroups: {Count} group(s) with variant spellings found", groups.Count);
        return groups;
    }

    public int MergeArtistGroup(ArtistMergeGroup group)
    {
        var variantSet = group.Variants.ToHashSet(StringComparer.Ordinal);
        var toUpdate = _tracks.Where(t => variantSet.Contains(t.Artist)).ToList();

        foreach (var track in toUpdate)
        {
            track.Artist = group.CanonicalName;
            _db.Update(track);
        }

        if (toUpdate.Count > 0) StateVersion++;
        Log.Information("[LibraryService] MergeArtistGroup: {Count} track(s) updated to canonical name '{Name}'",
            toUpdate.Count, group.CanonicalName);
        return toUpdate.Count;
    }

    internal static string NormalizeArtistKey(string artist)
    {
        var stripped = new string(artist.Where(c =>
            System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                != System.Globalization.UnicodeCategory.Format).ToArray());

        var normalized = stripped.Normalize(System.Text.NormalizationForm.FormKC);
        var collapsed = Regex.Replace(normalized.Trim(), @"\s+", " ");
        var joinerNormalized = ArtistSeparatorRegex.Replace(collapsed, " & ");

        return joinerNormalized.ToLowerInvariant();
    }

    public static List<string> SplitArtistCredits(string artist)
    {
        if (string.IsNullOrWhiteSpace(artist)) return new List<string>();

        return ArtistSeparatorRegex.Split(artist)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();
    }

    public int ForceCleanTitles()
    {
        int cleaned = 0;
        foreach (var track in _tracks)
        {
            if (track.TitleForceCleaned) continue;

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
            track.TitleForceCleaned = true;
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
                .OrderByDescending(t => HasVerifiedFile(t))
                .ThenByDescending(t => !string.IsNullOrEmpty(t.FilePath) && File.Exists(t.FilePath))
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