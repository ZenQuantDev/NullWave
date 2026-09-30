using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using NullWave.Helpers;
using NullWave.Models;
using NullWave.Services.Metadata;
using Serilog;

namespace NullWave.Services;

public partial class LibraryService
{
    private void CleanupBadUrls()
    {
        List<Track> bad;
        lock (_tracksLock) { bad = _tracks.Where(t => !string.IsNullOrEmpty(t.Url) && !SourceDetector.IsPlayableUrl(t.Url) && string.IsNullOrEmpty(t.FilePath)).ToList(); }
        if (bad.Count == 0) return;

        foreach (var t in bad)
            Log.Warning("[LibraryService] Removing unplayable URL from DB: {Url}", t.Url);

        lock (_tracksLock) { foreach (var t in bad) { _tracks.Remove(t); _db.Delete(t.Id); } }
        StateVersion++;
        Log.Information("[LibraryService] Cleaned {Count} bad tracks from DB", bad.Count);
    }

    // PERF: collects results off-UI, then applies them in ONE UI-thread wave via
    // ApplyArtBatch. No per-track PropertyChanged storm, no LibraryChanged rebuild.
    private async Task BackfillAlbumArt()
    {
        if (_metadata == null) return;

        List<Track> targets;
        lock (_tracksLock)
        {
            targets = _tracks.Where(t => string.IsNullOrEmpty(t.AlbumArtPath) &&
                                         !string.IsNullOrEmpty(t.FilePath) &&
                                         File.Exists(t.FilePath)).ToList();
        }
        if (targets.Count == 0) return;

        // Let the window finish its first paints before touching the disk
        await Task.Delay(1500);

        var results = new List<(Track Track, string Path)>();
        int count = 0;
        foreach (var t in targets)
        {
            try
            {
                var art = await _metadata.ExtractAlbumArtAsync(t.FilePath!);
                if (!string.IsNullOrEmpty(art)) results.Add((t, art));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[LibraryService] Art backfill failed for {Path}", t.FilePath);
            }
            if (++count % 25 == 0) await Task.Delay(16);
        }

        ApplyArtBatch(results);
    }

    private void BackfillYouTubeThumbnails()
    {
        List<Track> yt;
        lock (_tracksLock) { yt = _tracks.Where(t => t.Source == TrackSource.YouTube && string.IsNullOrEmpty(t.AlbumArtPath) && !string.IsNullOrEmpty(t.Url)).ToList(); }
        if (yt.Count == 0) return;

        var results = new List<(Track Track, string Path)>();
        foreach (var t in yt)
        {
            try
            {
                var id = YouTubeMetadataFetcher.ExtractYouTubeId(t.Url!);
                if (string.IsNullOrEmpty(id)) continue;
                var path = YouTubeMetadataFetcher.FetchThumbnailAsync(id).GetAwaiter().GetResult();
                if (!string.IsNullOrEmpty(path)) results.Add((t, path));
            }
            catch (Exception ex) { Log.Warning(ex, "[LibraryService] YT thumb backfill failed for {Title}", t.Title); }
        }

        ApplyArtBatch(results);
    }

    private void BackfillSoundCloudThumbnails()
    {
        List<Track> sc;
        lock (_tracksLock) { sc = _tracks.Where(t => t.Source == TrackSource.SoundCloud && string.IsNullOrEmpty(t.AlbumArtPath) && !string.IsNullOrEmpty(t.Url)).ToList(); }
        if (sc.Count == 0) return;

        var fetcher = new SoundCloudMetadataFetcher();
        var results = new List<(Track Track, string Path)>();
        var metaUpdates = new List<(Track Track, string? Title, string? Artist)>();

        foreach (var t in sc)
        {
            try
            {
                var (title, artist, thumb, _) = fetcher.FetchAsync(t.Url!).GetAwaiter().GetResult();
                if (!string.IsNullOrEmpty(thumb) && string.IsNullOrEmpty(t.AlbumArtPath)) results.Add((t, thumb));

                string? newTitle = null, newArtist = null;
                if ((t.Title == t.Url || string.IsNullOrWhiteSpace(t.Title)) && !string.IsNullOrWhiteSpace(title)) newTitle = title;
                if ((t.Artist == "Unknown" || string.IsNullOrWhiteSpace(t.Artist)) && !string.IsNullOrWhiteSpace(artist)) newArtist = artist;
                if (newTitle != null || newArtist != null) metaUpdates.Add((t, newTitle, newArtist));
            }
            catch (Exception ex) { Log.Warning(ex, "[LibraryService] SC backfill failed for {Title}", t.Title); }
        }

        if (results.Count == 0 && metaUpdates.Count == 0) return;

        // ONE UI-thread wave for all mutations, then ONE DB transaction.
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var (t, p) in results) t.AlbumArtPath = p;
            foreach (var (t, ti, ar) in metaUpdates)
            {
                if (ti != null) t.Title = ti;
                if (ar != null) t.Artist = ar;
            }
            StateVersion++;
            _ = Task.Run(() => _db.RunInTransaction(() =>
            {
                foreach (var (t, _) in results) _db.Update(t);
                foreach (var (t, _, _) in metaUpdates) _db.Update(t);
            }));
        });
    }

    /// <summary>
    /// Applies fetched art in a single UI-thread wave and persists in one transaction.
    /// Deliberately does NOT raise LibraryChanged: that event triggers
    /// LibraryViewModel.Refresh() -> Tracks.ReplaceAll(), which rebuilds every row
    /// container and invalidates the whole window (the full-screen flash we captured).
    /// Per-row PropertyChanged is sufficient: rows bind AlbumArtPath directly.
    /// </summary>
    private void ApplyArtBatch(List<(Track Track, string Path)> results)
    {
        if (results.Count == 0) return;

        Dispatcher.UIThread.Post(() =>
        {
            foreach (var (t, p) in results) t.AlbumArtPath = p;
            StateVersion++;
            _ = Task.Run(() => _db.RunInTransaction(() =>
            {
                foreach (var (t, _) in results) _db.Update(t);
            }));
        });
    }

    /// <summary>
    /// Surgical cache heal: deletes every cached YouTube thumbnail (yt_*.jpg),
    /// invalidates its decoded cache entries, nulls AlbumArtPath on the tracks that
    /// pointed at them, and persists in one transaction. Caller then runs
    /// RebackfillThumbnails() to regenerate via the aspect-preserving ladder.
    /// Embedded (hex-named) art is untouched - those are true 1:1 album covers.
    /// </summary>
    public int RefetchYouTubeThumbnails()
    {
        List<Track> affected;
        lock (_tracksLock)
        {
            affected = _tracks.Where(t => !string.IsNullOrEmpty(t.AlbumArtPath) &&
                                          Path.GetFileName(t.AlbumArtPath!)
                                              .StartsWith("yt_", StringComparison.OrdinalIgnoreCase))
                              .ToList();
        }
        if (affected.Count == 0) return 0;

        int deleted = 0;
        foreach (var t in affected)
        {
            var path = t.AlbumArtPath!;
            try { if (File.Exists(path)) { File.Delete(path); deleted++; } } catch { }
            BitmapCacheService.InvalidatePath(path);
            t.AlbumArtPath = null;
        }

        _db.RunInTransaction(() => { foreach (var t in affected) _db.Update(t); });
        StateVersion++;
        Dispatcher.UIThread.Post(() => LibraryChanged?.Invoke(this, EventArgs.Empty));
        Log.Information("[LibraryService] YouTube thumb heal: cleared {Count} path(s), deleted {Deleted} file(s)",
            affected.Count, deleted);
        return affected.Count;
    }

    public void RebackfillThumbnails() { BackfillYouTubeThumbnails(); BackfillSoundCloudThumbnails(); }

    public async Task RefreshAlbumArtAsync(Track t)
    {
        if (_metadata == null || string.IsNullOrEmpty(t.FilePath)) return;
        t.AlbumArtPath = await _metadata.ExtractAlbumArtAsync(t.FilePath!);
        _db.Update(t);
        StateVersion++;
        OnLibraryChanged();
    }

    public int ClearAllArt()
    {
        List<Track> targets;
        lock (_tracksLock) { targets = _tracks.Where(t => !string.IsNullOrEmpty(t.AlbumArtPath)).ToList(); }
        if (targets.Count == 0) return 0;

        // Persist off-UI in one transaction first...
        foreach (var t in targets) t.AlbumArtPath = null;   // model-only, off-UI thread
        _db.RunInTransaction(() => { foreach (var t in targets) _db.Update(t); });
        StateVersion++;

        // ...then ONE UI wave so rows drop their art in a single invalidation pass.
        Dispatcher.UIThread.Post(() => LibraryChanged?.Invoke(this, EventArgs.Empty));

        try { if (Directory.Exists(NullWavePaths.ArtCacheDir)) foreach (var f in Directory.EnumerateFiles(NullWavePaths.ArtCacheDir)) try { File.Delete(f); } catch { } } catch { }
        return targets.Count;
    }

    public int BackfillDurations()
    {
        var fetcher = new LocalMetadataFetcher();
        int updated = 0;
        var targets = GetAll().Where(t => t.Duration == TimeSpan.Zero && !string.IsNullOrEmpty(t.FilePath) && File.Exists(t.FilePath)).ToList();
        foreach (var t in targets)
        {
            var (_, _, dur) = fetcher.Fetch(t.FilePath!);
            if (dur > TimeSpan.Zero) { t.Duration = dur; Update(t); updated++; }
        }
        return updated;
    }

    public (int total, int missing, int removed) RepairPaths(bool removeDeadEntries = false)
    {
        var withPath = GetAll().Where(t => !string.IsNullOrEmpty(t.FilePath)).ToList();
        int missing = 0, removed = 0;
        foreach (var t in withPath)
        {
            if (File.Exists(t.FilePath)) continue;
            missing++;
            if (removeDeadEntries) { t.FilePath = null; _db.Update(t); removed++; }
        }
        if (removed > 0) StateVersion++;
        return (withPath.Count, missing, removed);
    }

    public int ReimportAssets(string dir)
    {
        if (!Directory.Exists(dir)) return 0;
        var exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".mp3", ".flac", ".m4a", ".ogg", ".wav", ".aac", ".opus" };
        var files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Where(f => exts.Contains(Path.GetExtension(f))).ToList();
        var candidates = GetAll().Where(t => string.IsNullOrEmpty(t.FilePath) || !File.Exists(t.FilePath)).ToList();
        var matchedIds = new HashSet<Guid>(); var matchedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var toUpdate = new List<Track>();

        foreach (var f in files)
        {
            var name = Path.GetFileNameWithoutExtension(f);
            var exact = candidates.FirstOrDefault(t => !matchedIds.Contains(t.Id) && string.Equals(t.Title, name, StringComparison.OrdinalIgnoreCase));
            if (exact != null) { exact.FilePath = f; matchedIds.Add(exact.Id); matchedFiles.Add(f); toUpdate.Add(exact); continue; }
        }
        foreach (var f in files)
        {
            if (matchedFiles.Contains(f)) continue;
            var fTok = Tokenize(Path.GetFileNameWithoutExtension(f));
            if (fTok.Count == 0) continue;
            var match = candidates.FirstOrDefault(t => !matchedIds.Contains(t.Id) && Tokenize(t.Title).Count >= 2 && Tokenize(t.Title).IsSubsetOf(fTok));
            if (match != null) { match.FilePath = f; matchedIds.Add(match.Id); matchedFiles.Add(f); toUpdate.Add(match); }
        }
        foreach (var t in toUpdate) _db.Update(t);
        if (toUpdate.Count > 0) StateVersion++;
        return toUpdate.Count;
    }

    public (int Checked, List<LinkMismatch> Mismatches) VerifyLinks()
    {
        var mismatches = new List<LinkMismatch>();
        if (_metadata == null) return (0, mismatches);
        var withFile = GetAll().Where(t => !string.IsNullOrEmpty(t.FilePath) && File.Exists(t.FilePath)).ToList();
        foreach (var t in withFile)
        {
            try
            {
                var (eT, eA, _) = _metadata.FetchFromLocalFile(t.FilePath!);
                if (string.IsNullOrWhiteSpace(eT) || TitlesLooselyMatch(t.Title, eT, t.Artist, eA)) continue;
                mismatches.Add(new LinkMismatch(t.Id, t.Title, t.Artist, eT, eA, t.FilePath!));
            }
            catch { }
        }
        return (withFile.Count, mismatches);
    }

    public List<ArtistMergeGroup> FindSimilarArtistGroups()
    {
        return GetAll().Where(t => !string.IsNullOrWhiteSpace(t.Artist))
            .GroupBy(t => NormalizeArtistKey(t.Artist))
            .Where(g => g.Select(t => t.Artist).Distinct(StringComparer.Ordinal).Count() > 1)
            .Select(g => {
                var v = g.GroupBy(t => t.Artist, StringComparer.Ordinal).Select(vg => (Name: vg.Key, Count: vg.Count())).OrderByDescending(x => x.Count).ThenBy(x => x.Name).ToList();
                return new ArtistMergeGroup(v[0].Name, v.Select(x => x.Name).ToList(), g.Count());
            }).OrderByDescending(g => g.TotalTracks).ToList();
    }

    public int MergeArtistGroup(ArtistMergeGroup group)
    {
        var set = group.Variants.ToHashSet(StringComparer.Ordinal);
        var toUpdate = GetAll().Where(t => set.Contains(t.Artist)).ToList();
        foreach (var t in toUpdate) { t.Artist = group.CanonicalName; _db.Update(t); }
        if (toUpdate.Count > 0) StateVersion++;
        return toUpdate.Count;
    }

    public int ForceCleanTitles()
    {
        int cleaned = 0; var toUpdate = new List<Track>();
        foreach (var t in GetAll())
        {
            if (t.TitleForceCleaned && !TrackTitleParser.HasExoticSeparator(t.Title)) continue;
            var parsed = TrackTitleParser.TryParseArtistTitle(CleanYouTubeArtifacts(t.Title, false));
            if (parsed == null || string.IsNullOrWhiteSpace(parsed.Value.Artist) || string.IsNullOrWhiteSpace(parsed.Value.Title) ||
                (parsed.Value.Artist == t.Artist && parsed.Value.Title == t.Title) ||
                (!TitlesLooselyMatch(parsed.Value.Artist, t.Artist) && !(string.IsNullOrWhiteSpace(t.Artist) || t.Artist == "Unknown" || t.Artist.EndsWith("- Topic"))))
            { t.TitleForceCleaned = true; toUpdate.Add(t); continue; }

            t.Title = parsed.Value.Title; t.Artist = parsed.Value.Artist; t.TitleForceCleaned = true;
            toUpdate.Add(t); UpdateFileTags(t); cleaned++;
        }
        if (toUpdate.Count > 0) { _db.RunInTransaction(() => { foreach (var t in toUpdate) _db.Update(t); }); StateVersion++; }
        return cleaned;
    }

    public int ClearTagsForReSync()
    {
        int cleared = 0;
        foreach (var t in GetAll()) { if (t.Tags.Count == 0) continue; t.Tags.Clear(); _db.Update(t); cleared++; }
        if (cleared > 0) StateVersion++;
        return cleared;
    }

    public (int Scanned, int Orphaned, int Deleted, int Failed) SweepOrphanedFiles(string downloadsDir, bool dryRun = true)
    {
        if (!Directory.Exists(downloadsDir)) return (0, 0, 0, 0);
        var known = new HashSet<string>(GetAll().Where(t => !string.IsNullOrEmpty(t.FilePath)).Select(t => Path.GetFullPath(t.FilePath!)), StringComparer.OrdinalIgnoreCase);
        var files = Directory.GetFiles(downloadsDir, "*.*", SearchOption.TopDirectoryOnly).Where(f => f.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase)).ToList();
        int orphaned = 0, deleted = 0, failed = 0;
        foreach (var f in files)
        {
            var full = Path.GetFullPath(f);
            if (known.Contains(full)) continue;
            orphaned++;
            if (dryRun) continue;
            try { File.Delete(full); deleted++; } catch { failed++; }
        }
        return (files.Count, orphaned, deleted, failed);
    }

    public (int Scanned, int DuplicateGroups, int Removed) RemoveDuplicates(bool dryRun = true)
    {
        var allTracks = GetAll();
        int scannedCount = allTracks.Count;

        var groups = allTracks
            .GroupBy(t => (Title: t.Title.Trim().ToLowerInvariant(), Artist: t.Artist.Trim().ToLowerInvariant()))
            .Where(g => g.Count() > 1).ToList();

        int removed = 0;
        foreach (var group in groups)
        {
            var ordered = group.OrderByDescending(t => HasVerifiedFile(t)).ThenByDescending(t => !string.IsNullOrEmpty(t.FilePath) && File.Exists(t.FilePath))
                .ThenByDescending(t => t.PlayCount).ThenByDescending(t => t.IsFavorite).ThenBy(t => t.DateAdded).ToList();
            var keeper = ordered[0];
            foreach (var dup in ordered.Skip(1))
            {
                if (!dryRun) { lock (_tracksLock) _tracks.Remove(dup); _db.Delete(dup.Id); removed++; }
            }
        }
        if (removed > 0) StateVersion++;
        return (scannedCount, groups.Count, removed);
    }

    public (long BeforeKB, long AfterKB) VacuumDatabase()
    {
        var before = new FileInfo(_db.DbPath).Length / 1024;
        _db.Vacuum();
        return (before, new FileInfo(_db.DbPath).Length / 1024);
    }
}