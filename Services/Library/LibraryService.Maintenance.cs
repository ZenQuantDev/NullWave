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

public partial class LibraryService
{
    private void CleanupBadUrls()
    {
        List<Track> bad;
        lock (_tracksLock)
        {
            bad = _tracks.Where(t => !string.IsNullOrEmpty(t.Url)
                                     && !SourceDetector.IsPlayableUrl(t.Url)
                                     && string.IsNullOrEmpty(t.FilePath)
                                     && !t.IsOffline).ToList();
        }
        if (bad.Count == 0) return;

        foreach (var t in bad)
        {
            t.IsOffline = true;
            _db.Update(t);
            Log.Warning("[LibraryService] Found unplayable URL, marking as offline: {Url}", t.Url);
        }
        StateVersion++;
        Log.Information("[LibraryService] Marked {Count} bad tracks as offline in DB", bad.Count);
    }

    public int RelinkMissingFiles()
    {
        var missing = GetAll().Where(t => !string.IsNullOrEmpty(t.FilePath) && !File.Exists(t.FilePath)).ToList();
        if (missing.Count == 0) return 0;

        var pref = _prefs?.Current.DownloadDirectory;
        var downloadsDir = string.IsNullOrWhiteSpace(pref) ? NullWavePaths.DownloadsDir : pref;

        if (string.IsNullOrWhiteSpace(downloadsDir) || !Directory.Exists(downloadsDir)) return 0;

        var audioExts = new[] { ".mp3", ".m4a", ".flac", ".wav", ".ogg", ".opus" };
        var orphans = Directory.EnumerateFiles(downloadsDir, "*.*", SearchOption.AllDirectories)
            .Where(f => audioExts.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .ToList();

        int relinked = 0;

        foreach (var track in missing)
        {
            var urlId = ExtractYouTubeId(track.Url);
            var id8 = track.Id.ToString("N")[..8];
            var wanted = TrackNormalizer.NormalizeForCompare($"{track.Artist} - {track.Title}");

            var match = orphans.FirstOrDefault(f => MatchesRelinkCandidate(f, id8, urlId, wanted));

            if (match != null)
            {
                track.FilePath = match;
                _db.Update(track);
                orphans.Remove(match);
                relinked++;
                Log.Information("[LibraryService] Relinked track {Title} to {Path}", track.Title, match);
            }
        }

        if (relinked > 0) StateVersion++;
        Log.Information("[LibraryService] Auto-relinked {Count} missing tracks using filename suffixes.", relinked);
        return relinked;
    }

    public bool TryRelink(Track track)
    {
        if (track == null || string.IsNullOrEmpty(track.FilePath) || File.Exists(track.FilePath))
            return false;

        var pref = _prefs?.Current.DownloadDirectory;
        var downloadsDir = string.IsNullOrWhiteSpace(pref) ? NullWavePaths.DownloadsDir : pref;

        if (string.IsNullOrWhiteSpace(downloadsDir) || !Directory.Exists(downloadsDir))
            return false;

        var audioExts = new[] { ".mp3", ".m4a", ".flac", ".wav", ".ogg", ".opus" };
        var urlId = ExtractYouTubeId(track.Url);
        var id8 = track.Id.ToString("N")[..8];
        var wanted = TrackNormalizer.NormalizeForCompare($"{track.Artist} - {track.Title}");

        string? match = null;

        try
        {
            match = Directory.EnumerateFiles(downloadsDir, "*.*", SearchOption.AllDirectories)
                .Where(f => audioExts.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .FirstOrDefault(f => MatchesRelinkCandidate(f, id8, urlId, wanted));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[LibraryService] TryRelink scan failed for {Title}", track.Title);
            return false;
        }

        if (match == null) return false;

        track.FilePath = match;
        _db.Update(track);
        StateVersion++;

        Log.Information("[LibraryService] TryRelink repaired {Title} -> {Path}", track.Title, match);
        return true;
    }

    /// <summary>
    /// Matches either the legacy [nw-id8-videoId] format, the new FileNamer [nw-id8] format,
    /// or falls back to a normalized "Artist - Title" comparison.
    /// </summary>
    private static bool MatchesRelinkCandidate(string filePath, string id8, string? urlId, string wanted)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);

        // New FileNamer format: "... [nw-{id8}]"
        if (fileName.Contains($"[nw-{id8}]", StringComparison.OrdinalIgnoreCase)) return true;

        // Legacy format: "... [nw-{id8}-{videoId}]"
        if (fileName.Contains($"[nw-{id8}-", StringComparison.OrdinalIgnoreCase)) return true;

        // Legacy video-id-only suffix
        if (urlId != null && fileName.Contains($"-{urlId}]", StringComparison.OrdinalIgnoreCase)) return true;

        // Normalized-name fallback for files renamed by older Sync versions.
        // Guard: a track with empty artist/title has an empty wanted key, which would
        // match any other empty-named file. Only match when wanted is non-empty.
        return !string.IsNullOrWhiteSpace(wanted)
            && TrackNormalizer.NormalizeForCompare(fileName) == wanted;
    }

    private static string? ExtractYouTubeId(string? url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        var match = Regex.Match(url, @"(?:v=|\/)([0-9A-Za-z_-]{11})");
        return match.Success ? match.Groups[1].Value : null;
    }

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

        foreach (var t in targets) t.AlbumArtPath = null;   
        _db.RunInTransaction(() => { foreach (var t in targets) _db.Update(t); });
        StateVersion++;

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
            var (_, _, dur) = fetcher.FetchFromLocalFile(t.FilePath!);
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
                if (string.IsNullOrWhiteSpace(eT) || TrackNormalizer.LooselyMatch(t.Title, eT, t.Artist, eA)) continue;
                mismatches.Add(new LinkMismatch(t.Id, t.Title, t.Artist, eT, eA, t.FilePath!));
            }
            catch { }
        }
        return (withFile.Count, mismatches);
    }

    public List<ArtistMergeGroup> FindSimilarArtistGroups()
    {
        return GetAll().Where(t => !string.IsNullOrWhiteSpace(t.Artist))
            .GroupBy(t => TrackNormalizer.NormalizeArtistKey(t.Artist))
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
            if (t.TitleForceCleaned && !TrackNormalizer.HasExoticSeparator(t.Title)) continue;
            var cleanedTitle = TrackNormalizer.CleanTitle(t.Title);
            var parsed = TrackNormalizer.TrySplit(cleanedTitle);
            if (parsed == null || string.IsNullOrWhiteSpace(parsed.Value.Artist) || string.IsNullOrWhiteSpace(parsed.Value.Title) ||
                (parsed.Value.Artist == t.Artist && parsed.Value.Title == t.Title) ||
                (!TrackNormalizer.LooselyMatch(parsed.Value.Artist, t.Artist, "", "") && !TrackNormalizer.IsPlaceholderArtist(t.Artist)))
            { t.TitleForceCleaned = true; toUpdate.Add(t); continue; }

            t.Title = parsed.Value.Title; t.Artist = parsed.Value.Artist; t.TitleForceCleaned = true;
            toUpdate.Add(t);
            UpdateFileTags(t);
            cleaned++;
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

        var known = new HashSet<string>(
            GetAll().Where(t => !string.IsNullOrEmpty(t.FilePath)).Select(t => Path.GetFullPath(t.FilePath!)),
            StringComparer.OrdinalIgnoreCase);

        if (known.Count == 0)
        {
            Log.Warning("[LibraryService] SweepOrphanedFiles aborted: Database has 0 linked files. Refusing to delete downloads.");
            return (0, -1, 0, 0); 
        }

        var audioExts = new[] { ".mp3", ".m4a", ".flac", ".wav", ".ogg", ".opus" };
        var files = Directory.GetFiles(downloadsDir, "*.*", SearchOption.TopDirectoryOnly)
            .Where(f => audioExts.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .ToList();

        // SWEEP GUARD: Abort if DB paths are stale and it looks like everything is orphaned
        var orphanCount = files.Count(f => !known.Contains(Path.GetFullPath(f)));
        if (files.Count >= 10 && orphanCount * 2 > files.Count)
        {
            Log.Warning("[LibraryService] SweepOrphanedFiles aborted: {Orphans}/{Files} files look orphaned - DB paths are probably stale.", orphanCount, files.Count);
            return (files.Count, -1, 0, 0);
        }

        int orphaned = 0, deleted = 0, failed = 0;

        var orphansDir = Path.Combine(downloadsDir, "_orphans");

        foreach (var f in files)
        {
            var full = Path.GetFullPath(f);
            if (known.Contains(full)) continue;
            orphaned++;

            if (dryRun) continue;

            try
            {
                Directory.CreateDirectory(orphansDir);
                var dest = Path.Combine(orphansDir, Path.GetFileName(f));
                
                if (File.Exists(dest)) dest = Path.Combine(orphansDir, $"{Path.GetFileNameWithoutExtension(f)}_{Guid.NewGuid():N}{Path.GetExtension(f)}");
                
                File.Move(f, dest);
                deleted++;
            }
            catch { failed++; }
        }

        return (files.Count, orphaned, deleted, failed);
    }

    public (int Scanned, int DuplicateGroups, int Removed) RemoveDuplicates(bool dryRun = true)
    {
        var allTracks = GetAll();
        int scannedCount = allTracks.Count;

        var groups = allTracks
            .Where(t => !TrackNormalizer.IsPlaceholderTitle(t.Title) && !TrackNormalizer.IsPlaceholderArtist(t.Artist))
            .GroupBy(t => TrackNormalizer.IdentityKey(t.Title, t.Artist))
            .Where(g => g.Key != $"ta:|" && g.Count() > 1)
            .ToList();

        int removed = 0;

        foreach (var group in groups)
        {
            var ordered = group.OrderByDescending(t => HasVerifiedFile(t))
                .ThenByDescending(t => !string.IsNullOrEmpty(t.FilePath) && File.Exists(t.FilePath))
                .ThenByDescending(t => t.PlayCount)
                .ThenByDescending(t => t.IsFavorite)
                .ThenBy(t => t.DateAdded)
                .ToList();

            var keeper = ordered[0];

            foreach (var dup in ordered.Skip(1))
            {
                if (!dryRun)
                {
                    Remove(dup.Id); 
                    removed++;
                }
            }
        }

        return (scannedCount, groups.Count, removed);
    }

    public (long BeforeKB, long AfterKB) VacuumDatabase()
    {
        var before = new FileInfo(_db.DbPath).Length / 1024;
        _db.Vacuum();
        return (before, new FileInfo(_db.DbPath).Length / 1024);
    }
}