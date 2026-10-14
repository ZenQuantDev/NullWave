using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NullWave.Helpers;
using NullWave.Models;
using SQLite;
using Serilog;

namespace NullWave.Services;

public class DatabaseService : IDisposable
{
    private readonly SQLiteConnection _db;
    private readonly PreferencesService? _prefs;
    private bool _disposed;
    private bool _ftsAvailable;
    private readonly CancellationTokenSource _migrationCts = new();
    public string DbPath { get; }
    
    /// <summary>Current schema version stored in SQLite's user_version pragma.</summary>
    public int SchemaVersion => _db.ExecuteScalar<int>("PRAGMA user_version;");

    public DatabaseService(PreferencesService? prefs = null)
    {
        _prefs = prefs;
        var path = NullWavePaths.DatabasePath;
        DbPath = path;

        // Ensure the target directory exists before any file operations
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // 0. Apply pending restore (if user clicked "Restore" in UI and restarted)
        ApplyPendingRestore(path);

        // 1. Safety Net: Migrate from legacy path if current DB is missing
        MigrateLegacyDatabaseIfNeeded(path);

        // 2. Time Machine: Create a rolling backup before opening/migrating
        CreateRollingBackupIfNeeded(path);

        _db = new SQLiteConnection(path);

        // FIX: Use ExecuteScalar instead of Execute because this PRAGMA returns a row ("wal")
        _db.ExecuteScalar<string>("PRAGMA journal_mode=WAL;");

        _db.CreateTable<TrackRecord>();
        _db.CreateTable<PlaylistRecord>();
        _db.CreateTable<PlaylistTrackRecord>();
        _db.CreateTable<PlaylistFolderRecord>();

        MigrateSchema();
        EnsureFts5Index();

        Log.Information("[DatabaseService] Opened DB at {Path} with WAL mode enabled", path);
    }

    private void EnsureFts5Index()
    {
        try
        {
            var exists = _db.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='TracksFts';") > 0;

            _db.RunInTransaction(() =>
            {
                CreateFtsTableIfMissing();
                CreateFtsTriggers();
                if (!exists) BackfillFtsIndex();
            });

            _ftsAvailable = true;
            if (!exists) Log.Information("[DatabaseService] FTS5 search index created and backfilled");
        }
        catch (Exception ex)
        {
            _ftsAvailable = false;
            Log.Warning(ex, "[DatabaseService] FTS5 index initialization failed; search will use in-memory matching");
        }
    }

    private void CreateFtsTableIfMissing()
    {
        _db.Execute(@"
            CREATE VIRTUAL TABLE IF NOT EXISTS TracksFts USING fts5(
                Title,
                Artist,
                Album,
                TagsRaw,
                content='Tracks',
                content_rowid='rowid'
            );");
    }

    private void CreateFtsTriggers()
    {
        _db.Execute(@"
            CREATE TRIGGER IF NOT EXISTS TracksFts_Insert AFTER INSERT ON Tracks BEGIN
                INSERT INTO TracksFts (rowid, Title, Artist, Album, TagsRaw)
                VALUES (new.rowid, new.Title, new.Artist, new.Album, new.TagsRaw);
            END;");

        _db.Execute(@"
            CREATE TRIGGER IF NOT EXISTS TracksFts_Update AFTER UPDATE ON Tracks BEGIN
                INSERT INTO TracksFts (TracksFts, rowid, Title, Artist, Album, TagsRaw)
                VALUES ('delete', old.rowid, old.Title, old.Artist, old.Album, old.TagsRaw);
                INSERT INTO TracksFts (rowid, Title, Artist, Album, TagsRaw)
                VALUES (new.rowid, new.Title, new.Artist, new.Album, new.TagsRaw);
            END;");

        _db.Execute(@"
            CREATE TRIGGER IF NOT EXISTS TracksFts_Delete AFTER DELETE ON Tracks BEGIN
                INSERT INTO TracksFts (TracksFts, rowid, Title, Artist, Album, TagsRaw)
                VALUES ('delete', old.rowid, old.Title, old.Artist, old.Album, old.TagsRaw);
            END;");
    }

    private void BackfillFtsIndex()
    {
        _db.Execute(@"
            INSERT INTO TracksFts (TracksFts) VALUES ('rebuild');");
    }

    private sealed class FtsResult
    {
        public string Id { get; set; } = string.Empty;
    }

    public bool HasFtsIndex()
    {
        return _db.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='TracksFts';") > 0;
    }

    public HashSet<Guid>? SearchFts(string query)
    {
        if (!_ftsAvailable) return null;
        var matchQuery = BuildFtsQuery(query);
        if (string.IsNullOrEmpty(matchQuery)) return null;

        try
        {
            var results = _db.Query<FtsResult>(@"
                SELECT Tracks.Id AS Id
                FROM TracksFts
                JOIN Tracks ON Tracks.rowid = TracksFts.rowid
                WHERE TracksFts MATCH ?", matchQuery);
            return results
                .Select(result => Guid.TryParse(result.Id, out var id) ? id : Guid.Empty)
                .Where(id => id != Guid.Empty)
                .ToHashSet();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[DatabaseService] FTS5 search failed for query {Query}", query);
            return null;
        }
    }

    private static string BuildFtsQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return string.Empty;

        var words = new string(query.Select(character => char.IsLetterOrDigit(character) ? character : ' ').ToArray())
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var terms = words.Select(term => $"\"{term}\"*");

        // FTS5 treats space-separated terms as AND
        return string.Join(" ", terms);
    }

    public void RebuildFtsIndex()
    {
        try
        {
            _db.RunInTransaction(() =>
            {
                _db.Execute("DROP TRIGGER IF EXISTS TracksFts_Insert;");
                _db.Execute("DROP TRIGGER IF EXISTS TracksFts_Update;");
                _db.Execute("DROP TRIGGER IF EXISTS TracksFts_Delete;");
                _db.Execute("DROP TABLE IF EXISTS TracksFts;");
                CreateFtsTableIfMissing();
                CreateFtsTriggers();
                BackfillFtsIndex();
            });
            _ftsAvailable = true;
            Log.Information("[DatabaseService] FTS5 search index rebuilt");
        }
        catch
        {
            _ftsAvailable = false;
            throw;
        }
    }

    private void ApplyPendingRestore(string currentPath)
    {
        var restorePath = currentPath + ".restoring";
        if (!File.Exists(restorePath)) return;

        var preRestore = currentPath + ".pre-restore";
        try
        {
            if (!IsValidDatabase(restorePath))
            {
                var rejected = $"{restorePath}.rejected-{DateTime.Now:yyyyMMdd-HHmmss}";
                File.Move(restorePath, rejected);
                Log.Error("[DatabaseService] Restore file failed the integrity check; current database kept. Moved to {Path}", rejected);
                return;
            }

            if (File.Exists(currentPath))
            {
                // Keep the previous database instead of deleting it
                File.Move(currentPath, preRestore, overwrite: true);
                MoveIfExists(currentPath + "-wal", preRestore + "-wal");
                MoveIfExists(currentPath + "-shm", preRestore + "-shm");
            }

            File.Move(restorePath, currentPath);
            Log.Information("[DatabaseService] Restore applied. Previous database kept at {Path}", preRestore);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[DatabaseService] Failed to apply pending restore.");
            try
            {
                // Roll back if we had already moved the current database away
                if (!File.Exists(currentPath) && File.Exists(preRestore))
                {
                    File.Move(preRestore, currentPath);
                    MoveIfExists(preRestore + "-wal", currentPath + "-wal");
                    MoveIfExists(preRestore + "-shm", currentPath + "-shm");
                }
            }
            catch (Exception rollbackEx)
            {
                Log.Error(rollbackEx, "[DatabaseService] Rollback failed; the previous database is at {Path}", preRestore);
            }
        }
    }

    private static void MoveIfExists(string from, string to)
    {
        if (File.Exists(from)) File.Move(from, to, overwrite: true);
    }

    internal static bool IsValidDatabase(string path)
    {
        try
        {
            using var conn = new SQLiteConnection(path, SQLiteOpenFlags.ReadOnly);
            var integrity = conn.ExecuteScalar<string>("PRAGMA integrity_check;");
            var hasTracks = conn.ExecuteScalar<int>(
                "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='Tracks';");
            return string.Equals(integrity, "ok", StringComparison.OrdinalIgnoreCase) && hasTracks == 1;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[DatabaseService] Could not validate database file {Path}", path);
            return false;
        }
    }

    private void MigrateLegacyDatabaseIfNeeded(string currentPath)
    {
        try
        {
            if (File.Exists(currentPath)) return; // Current DB exists, no migration needed

            string legacyPath = NullWavePaths.LegacyDatabasePath;

            // Ensure we aren't comparing the exact same path (e.g. on Linux where DataDir == LegacyDataDir)
            if (File.Exists(legacyPath) &&
                !string.Equals(Path.GetFullPath(legacyPath), Path.GetFullPath(currentPath), StringComparison.OrdinalIgnoreCase))
            {
                Log.Information("[DatabaseService] Found legacy database at {LegacyPath}. Migrating to {CurrentPath}...", legacyPath, currentPath);

                File.Copy(legacyPath, currentPath, overwrite: false);

                // Also copy WAL and SHM if they exist to prevent corruption
                if (File.Exists(legacyPath + "-wal")) File.Copy(legacyPath + "-wal", currentPath + "-wal", overwrite: false);
                if (File.Exists(legacyPath + "-shm")) File.Copy(legacyPath + "-shm", currentPath + "-shm", overwrite: false);

                Log.Information("[DatabaseService] Legacy database migration complete. Your tracks are back!");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[DatabaseService] Failed to migrate legacy database.");
        }
    }

    private void CreateRollingBackupIfNeeded(string dbPath)
    {
        try
        {
            if (!File.Exists(dbPath)) return;
            if (new FileInfo(dbPath).Length < 10240) return;
            if (!(_prefs?.Current.EnableAutoBackups ?? true)) return;

            int retention = Math.Max(1, _prefs?.Current.BackupRetentionCount ?? 3);
            string backupDir = NullWavePaths.BackupsDir;
            Directory.CreateDirectory(backupDir);

            if (!BackupIsDue(dbPath, backupDir, DateTime.Now)) return;

            string backupFileName = $"library-{DateTime.Now:yyyyMMdd-HHmmss}.db";
            string backupPath = Path.Combine(backupDir, backupFileName);

            File.Copy(dbPath, backupPath, overwrite: false);
            if (File.Exists(dbPath + "-wal")) File.Copy(dbPath + "-wal", backupPath + "-wal", overwrite: false);
            if (File.Exists(dbPath + "-shm")) File.Copy(dbPath + "-shm", backupPath + "-shm", overwrite: false);

            Log.Information("[DatabaseService] Created rolling backup: {BackupFile}", backupFileName);
            PruneOldBackups(backupDir, retention);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[DatabaseService] Failed to create rolling backup.");
        }
    }

    /// <summary>A backup is due if none exists, or the newest is over 24 hours old AND the database changed since.</summary>
    internal static bool BackupIsDue(string dbPath, string backupDir, DateTime now)
    {
        var newest = Directory.GetFiles(backupDir, "library-*.db")
            .Select(f => TryParseBackupTime(f))
            .Where(t => t.HasValue)
            .OrderByDescending(t => t!.Value)
            .FirstOrDefault();

        if (newest == null) return true;
        if (now - newest.Value < TimeSpan.FromHours(24)) return false;

        var lastChange = new[] { dbPath, dbPath + "-wal" }
            .Where(f => File.Exists(f))
            .Max(f => File.GetLastWriteTime(f));

        return lastChange > newest.Value;
    }

    internal static DateTime? TryParseBackupTime(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);          // library-20260920-093000
        const string prefix = "library-";
        if (!name.StartsWith(prefix, StringComparison.Ordinal)) return null;
        
        return DateTime.TryParseExact(name[prefix.Length..], "yyyyMMdd-HHmmss",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? time
            : null;
    }

    private void PruneOldBackups(string backupDir, int retentionCount)
    {
        try
        {
            var backupFiles = Directory.GetFiles(backupDir, "library-*.db")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.Name, StringComparer.Ordinal)   // Prune by filename timestamp, not CreationTime
                .ToList();

            if (backupFiles.Count > retentionCount)
            {
                var toDelete = backupFiles.Skip(retentionCount);
                foreach (var file in toDelete)
                {
                    file.Delete();
                    if (File.Exists(file.FullName + "-wal")) File.Delete(file.FullName + "-wal");
                    if (File.Exists(file.FullName + "-shm")) File.Delete(file.FullName + "-shm");
                    Log.Debug("[DatabaseService] Pruned old backup: {FileName}", file.Name);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[DatabaseService] Failed to prune old backups.");
        }
    }

    /* <summary>
       Schema migrations run exactly once, in order, tracked by SQLite's user_version.
    </summary>  */
    private void MigrateSchema()
    {
        try
        {
            int currentVersion = _db.ExecuteScalar<int>("PRAGMA user_version;");
            
            // If version is 0, this might be an existing DB from before we introduced versioning.
            // Detect the current state based on which columns already exist.
            if (currentVersion == 0)
            {
                currentVersion = DetectCurrentVersion();
                if (currentVersion > 0)
                {
                    _db.Execute($"PRAGMA user_version = {currentVersion};");
                    Log.Information("[DatabaseService] Detected existing legacy schema at version {Version}", currentVersion);
                }
            }

            if (currentVersion < 1) { _db.Execute("ALTER TABLE Playlists ADD COLUMN CustomArtPath VARCHAR;"); currentVersion = 1; _db.Execute("PRAGMA user_version = 1;"); Log.Information("[DatabaseService] Migration 1: added Playlists.CustomArtPath"); }
            if (currentVersion < 2) { _db.Execute("ALTER TABLE Tracks ADD COLUMN DurationMs INTEGER DEFAULT 0;"); currentVersion = 2; _db.Execute("PRAGMA user_version = 2;"); Log.Information("[DatabaseService] Migration 2: added Tracks.DurationMs"); }
            if (currentVersion < 3) { _db.Execute("ALTER TABLE Tracks ADD COLUMN MediaType VARCHAR DEFAULT 'Music';"); currentVersion = 3; _db.Execute("PRAGMA user_version = 3;"); Log.Information("[DatabaseService] Migration 3: added Tracks.MediaType"); }
            if (currentVersion < 4) { _db.Execute("ALTER TABLE Tracks ADD COLUMN PlaybackPositionTicks INTEGER DEFAULT 0;"); currentVersion = 4; _db.Execute("PRAGMA user_version = 4;"); Log.Information("[DatabaseService] Migration 4: added Tracks.PlaybackPositionTicks"); }
            if (currentVersion < 5) { _db.Execute("ALTER TABLE Tracks ADD COLUMN Album VARCHAR;"); currentVersion = 5; _db.Execute("PRAGMA user_version = 5;"); Log.Information("[DatabaseService] Migration 5: added Tracks.Album"); }
            if (currentVersion < 6) { _db.Execute("ALTER TABLE Tracks ADD COLUMN TrackNumber INTEGER DEFAULT 0;"); currentVersion = 6; _db.Execute("PRAGMA user_version = 6;"); Log.Information("[DatabaseService] Migration 6: added Tracks.TrackNumber"); }
            if (currentVersion < 7) { _db.Execute("ALTER TABLE PlaylistTracks ADD COLUMN DateAdded DATETIME;"); currentVersion = 7; _db.Execute("PRAGMA user_version = 7;"); Log.Information("[DatabaseService] Migration 7: added PlaylistTracks.DateAdded"); }

            // 2.6 FIX: Radio repair migration. 
            // REMOVED the old buggy logic that guessed Radio based on URL containing "radio" or "stream".
            // NEW: Any track marked as Radio that actually has a local file or a known non-radio source is a misclassified music track.
            if (currentVersion < 8)
            {
                var repaired = _db.Execute("UPDATE Tracks SET MediaType = 'Music' WHERE MediaType = 'Radio' AND (FilePath IS NOT NULL OR Source IN ('YouTube', 'SoundCloud', 'Local', 'Spotify', 'LastFm'));");
                if (repaired > 0) Log.Information("[DatabaseService] Migration 8: Repaired {Count} misclassified Radio tracks back to Music", repaired);
                currentVersion = 8;
                _db.Execute("PRAGMA user_version = 8;");
            }

            Log.Information("[DatabaseService] Schema is at version {Version}", currentVersion);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[DatabaseService] Schema migration check failed");
        }

        // Path tokenization (Version 9) runs in the background to avoid UI freezes
        _ = Task.Run(() =>
        {
            try
            {
                var token = _migrationCts.Token;
                if (token.IsCancellationRequested || _disposed) return;

                int currentVersion = _db.ExecuteScalar<int>("PRAGMA user_version;");
                if (currentVersion >= 9) return; // Already tokenized

                token.ThrowIfCancellationRequested();
                bool dbUpdated = false;
                var rawTracks = _db.Table<TrackRecord>().ToList();
                foreach (var r in rawTracks)
                {
                    bool changed = false;
                    var tokFilePath = PathHelper.Tokenize(r.FilePath);
                    if (tokFilePath != r.FilePath) { r.FilePath = tokFilePath; changed = true; }

                    var tokArtPath = PathHelper.Tokenize(r.AlbumArtPath);
                    if (tokArtPath != r.AlbumArtPath) { r.AlbumArtPath = tokArtPath; changed = true; }

                    if (changed) { SaveTrackRecord(r); dbUpdated = true; }
                }

                var rawPlaylists = _db.Table<PlaylistRecord>().ToList();
                foreach (var p in rawPlaylists)
                {
                    var tokArt = PathHelper.Tokenize(p.CustomArtPath);
                    if (tokArt != p.CustomArtPath)
                    {
                        p.CustomArtPath = tokArt;
                        _db.InsertOrReplace(p);
                        dbUpdated = true;
                    }
                }
                
                if (dbUpdated) Log.Information("[DatabaseService] Migrated absolute paths to portable tokens.");
                
                _db.Execute("PRAGMA user_version = 9;");
                Log.Information("[DatabaseService] Schema advanced to version 9 (path tokenization complete)");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[DatabaseService] Path tokenization migration failed");
            }
        });
    }

    /// <summary>Inspects existing columns to determine the baseline schema version for legacy databases.</summary>
    private int DetectCurrentVersion()
    {
        var trackCols = _db.Query<ColumnInfo>("PRAGMA table_info(Tracks);").Select(c => c.Name.ToLowerInvariant()).ToHashSet();
        var plCols = _db.Query<ColumnInfo>("PRAGMA table_info(Playlists);").Select(c => c.Name.ToLowerInvariant()).ToHashSet();
        var ptCols = _db.Query<ColumnInfo>("PRAGMA table_info(PlaylistTracks);").Select(c => c.Name.ToLowerInvariant()).ToHashSet();

        int version = 0;
        if (plCols.Contains("customartpath")) version = 1;
        if (trackCols.Contains("durationms")) version = 2;
        if (trackCols.Contains("mediatype")) version = 3;
        if (trackCols.Contains("playbackpositionticks")) version = 4;
        if (trackCols.Contains("album")) version = 5;
        if (trackCols.Contains("tracknumber")) version = 6;
        if (ptCols.Contains("dateadded")) version = 7;
        return version;
    }

    private class ColumnInfo
    {
        public string Name { get; set; } = "";
    }

    public void Vacuum()
    {
        var sizeBefore = new FileInfo(DbPath).Length;
        _db.Execute("VACUUM;");
        var sizeAfter = new FileInfo(DbPath).Length;
        Log.Information("[DatabaseService] VACUUM complete: {Before}KB → {After}KB",
            sizeBefore / 1024, sizeAfter / 1024);
            
        // 2.7 FIX: Rebuild FTS index after VACUUM (cheap insurance against index corruption)
        if (_ftsAvailable)
        {
            try { _db.Execute("INSERT INTO TracksFts(TracksFts) VALUES('rebuild');"); }
            catch (Exception ex) { Log.Warning(ex, "[DatabaseService] FTS rebuild after VACUUM failed"); }
        }
    }

    public void BackupToPath(string targetPath)
    {
        if (File.Exists(targetPath)) File.Delete(targetPath);
        // VACUUM INTO requires SQLite 3.27+. sqlite-net-pcl bundles a recent version.
        _db.Execute($"VACUUM INTO '{targetPath.Replace("'", "''")}';");
        if (!IsValidDatabase(targetPath))
        {
            try { File.Delete(targetPath); } catch { }
            throw new InvalidOperationException("Backup validation failed.");
        }
    }

    public List<Track> LoadAll()
    {
        try
        {
            return _db.Table<TrackRecord>().ToList().Select(r => r.ToTrack()).ToList();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[DatabaseService] Failed to load tracks");
            return new List<Track>();
        }
    }

    public void Insert(Track track)
    {
        try { SaveTrackRecord(TrackRecord.FromTrack(track)); }
        catch (Exception ex) { Log.Error(ex, "[DatabaseService] Insert failed for {Title}", track.Title); }
    }

    public void Update(Track track)
    {
        try { SaveTrackRecord(TrackRecord.FromTrack(track)); }
        catch (Exception ex) { Log.Error(ex, "[DatabaseService] Update failed for {Title}", track.Title); }
    }

    private void SaveTrackRecord(TrackRecord record)
    {
        _db.RunInTransaction(() =>
        {
            if (_db.Find<TrackRecord>(record.Id) == null) _db.Insert(record);
            else _db.Update(record);
        });
    }

    public void Delete(Guid id)
    {
        try { _db.Delete<TrackRecord>(id.ToString()); }
        catch (Exception ex) { Log.Error(ex, "[DatabaseService] Delete failed for {Id}", id); }
    }

    public void RunInTransaction(Action action)
    {
        try { _db.RunInTransaction(action); }
        catch (Exception ex) { Log.Error(ex, "[DatabaseService] Transaction failed"); }
    }

    public void SavePlaylist(Playlist playlist)
    {
        try
        {
            var syncDb = _db;
            syncDb.RunInTransaction(() =>
            {
                syncDb.InsertOrReplace(PlaylistRecord.FromPlaylist(playlist));
                syncDb.Execute("DELETE FROM PlaylistTracks WHERE PlaylistId = ?", playlist.Id.ToString());

                for (int i = 0; i < playlist.Tracks.Count; i++)
                {
                    var trackId = playlist.Tracks[i].Id;
                    syncDb.Insert(new PlaylistTrackRecord
                    {
                        PlaylistId = playlist.Id.ToString(),
                        TrackId = trackId.ToString(),
                        SortOrder = i,
                        DateAdded = playlist.TrackDateAdded.TryGetValue(trackId, out var d) ? d : playlist.DateCreated
                    });
                }
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save playlist to database.");
        }
    }

    public List<Playlist> LoadPlaylists(List<Track> entireLibrary)
    {
        var playlists = new List<Playlist>();
        try
        {
            var trackMap = entireLibrary.ToDictionary(t => t.Id.ToString());
            var syncDb = _db;

            foreach (var plRecord in syncDb.Table<PlaylistRecord>().ToList())
            {
                var pList = new Playlist
                {
                    Id = Guid.Parse(plRecord.Id),
                    Name = plRecord.Name,
                    Description = plRecord.Description,
                    FolderId = string.IsNullOrWhiteSpace(plRecord.FolderId)
                        ? null
                        : Guid.TryParse(plRecord.FolderId, out var folderId) ? folderId : null,
                    CustomArtPath = plRecord.CustomArtPath
                };

                var dbTracks = syncDb.Table<PlaylistTrackRecord>()
                    .Where(pt => pt.PlaylistId == plRecord.Id)
                    .OrderBy(pt => pt.SortOrder)
                    .ToList();

                foreach (var pt in dbTracks)
                {
                    if (trackMap.TryGetValue(pt.TrackId, out var track))
                    {
                        pList.Tracks.Add(track);
                        pList.TrackDateAdded[track.Id] = pt.DateAdded == default ? pList.DateCreated : pt.DateAdded;
                    }
                }

                playlists.Add(pList);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load playlists from database.");
        }

        return playlists;
    }

    public void SavePlaylistFolder(PlaylistFolder folder)
    {
        try
        {
            _db.InsertOrReplace(new PlaylistFolderRecord
            {
                Id = folder.Id.ToString(),
                Name = folder.Name,
                CreatedAt = folder.DateCreated
            });
        }
        catch (Exception ex) { Log.Error(ex, "Failed to save playlist folder to database."); }
    }

    public List<PlaylistFolder> LoadPlaylistFolders()
    {
        var folders = new List<PlaylistFolder>();
        try
        {
            foreach (var record in _db.Table<PlaylistFolderRecord>().ToList())
            {
                folders.Add(new PlaylistFolder
                {
                    Id = Guid.TryParse(record.Id, out var id) ? id : Guid.NewGuid(),
                    Name = record.Name,
                    DateCreated = record.CreatedAt
                });
            }
        }
        catch (Exception ex) { Log.Error(ex, "Failed to load playlist folders from database."); }

        return folders;
    }

    public void DeletePlaylistFolder(Guid id)
    {
        try { _db.Delete<PlaylistFolderRecord>(id.ToString()); }
        catch (Exception ex) { Log.Error(ex, "Failed to delete playlist folder from database."); }
    }

    public void DeletePlaylist(Guid id)
    {
        try
        {
            var strId = id.ToString();
            _db.Delete<PlaylistRecord>(strId);
            _db.Execute("DELETE FROM PlaylistTracks WHERE PlaylistId = ?", strId);
        }
        catch (Exception ex) { Log.Error(ex, "Failed to delete playlist from database."); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        // Stop background migrations before closing the connection to prevent AccessViolationExceptions in tests
        _migrationCts.Cancel();
        _migrationCts.Dispose();
        
        // 2.7 FIX: Flush WAL to main DB file before closing to ensure all writes are persisted
        try { _db.ExecuteScalar<string>("PRAGMA wal_checkpoint(TRUNCATE);"); }
        catch { /* best effort */ }
        
        _db.Close();
    }
}