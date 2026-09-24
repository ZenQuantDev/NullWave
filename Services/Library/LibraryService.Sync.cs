using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NullWave.Models;
using Serilog;

namespace NullWave.Services;

public partial class LibraryService
{
    public FileSyncReport SyncLocalFilesWithLibrary(bool dryRun, string? currentlyPlayingPath)
    {
        int scanned = 0, retagged = 0, renamed = 0, skipped = 0, failed = 0;
        foreach (var t in GetAll())
        {
            if (string.IsNullOrEmpty(t.FilePath) || !File.Exists(t.FilePath)) continue;
            scanned++;
            try
            {
                var (wasRetagged, wasRenamed) = SyncOne(t, dryRun, currentlyPlayingPath);
                if (wasRetagged) retagged++;
                if (wasRenamed) renamed++;
            }
            catch (IOException) { skipped++; }
            catch (Exception ex) { failed++; Log.Warning(ex, "[LibraryService] Sync failed for {Path}", t.FilePath); }
        }
        if (!dryRun && (retagged > 0 || renamed > 0)) StateVersion++;
        return new FileSyncReport(scanned, retagged, renamed, skipped, failed);
    }

    public void NormalizeLocalFile(Track t)
    {
        if (string.IsNullOrEmpty(t.FilePath) || !File.Exists(t.FilePath)) return;
        try { SyncOne(t, false, null); StateVersion++; } catch { }
    }

    private (bool Retagged, bool Renamed) SyncOne(Track track, bool dryRun, string? currentlyPlayingPath)
    {
        if (currentlyPlayingPath != null && string.Equals(Path.GetFullPath(track.FilePath!), Path.GetFullPath(currentlyPlayingPath), StringComparison.OrdinalIgnoreCase))
            return (false, false);

        bool retagged = false, renamed = false;
        if (_metadata != null)
        {
            var (fT, fA, _) = _metadata.FetchFromLocalFile(track.FilePath!);
            if (!TitlesLooselyMatch(track.Title, fT, track.Artist, fA) && TitlesLooselyMatch(track.Title, fA, track.Artist, fT))
            {
                track.Title = CleanYouTubeArtifacts(fT); track.Artist = CleanYouTubeArtifacts(fA);
                if (!dryRun) _db.Update(track);
                return (true, false);
            }
            if (!string.Equals(fT, track.Title, StringComparison.Ordinal) || !string.Equals(fA, track.Artist, StringComparison.Ordinal))
            {
                if (!dryRun) _metadata.WriteTagsToFile(track.FilePath!, track.Title, track.Artist);
                retagged = true;
            }
        }

        var dir = Path.GetDirectoryName(track.FilePath!)!;
        var ext = Path.GetExtension(track.FilePath!);
        var name = SanitizeFileName($"{track.Artist} - {track.Title}") + ext;
        var target = Path.Combine(dir, name);

        if (!string.Equals(Path.GetFileName(track.FilePath!), name, StringComparison.OrdinalIgnoreCase))
        {
            if (File.Exists(target)) return (retagged, false);
            if (!dryRun) { File.Move(track.FilePath!, target); track.FilePath = target; _db.Update(track); }
            renamed = true;
        }
        return (retagged, renamed);
    }

    public void UpdateFileTags(Track track)
    {
        if (_metadata == null || string.IsNullOrEmpty(track.FilePath) || !File.Exists(track.FilePath)) return;
        _metadata.WriteTagsToFile(track.FilePath, track.Title, track.Artist);
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) ? ' ' : c).ToArray()).Trim();
    }
}