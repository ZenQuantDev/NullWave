using System;
using System.Globalization;
using System.IO;
using System.Linq;
using NullWave.Helpers;
using SQLite;
using Serilog;

namespace NullWave.Services;

public partial class DatabaseService
{
    private void ApplyPendingRestore(string currentPath)
    {
        var restorePath = currentPath + ".restoring";
        if (!File.Exists(restorePath)) return;

        var preRestore = currentPath + ".pre-restore";
        try
        {
            if (!IsValidDatabase(restorePath))
            {
                var rejected = $"{restorePath}.rejected-{UtcTime.FileStamp()}";
                File.Move(restorePath, rejected);
                Log.Error("[DatabaseService] Restore file failed the integrity check; current database kept. Moved to {Path}", rejected);
                return;
            }

            if (File.Exists(currentPath))
            {
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
            if (File.Exists(currentPath)) return;

            string legacyPath = NullWavePaths.LegacyDatabasePath;

            if (File.Exists(legacyPath) &&
                !string.Equals(Path.GetFullPath(legacyPath), Path.GetFullPath(currentPath), StringComparison.OrdinalIgnoreCase))
            {
                Log.Information("[DatabaseService] Found legacy database at {LegacyPath}. Migrating to {CurrentPath}...", legacyPath, currentPath);

                File.Copy(legacyPath, currentPath, overwrite: false);

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

            if (!BackupIsDue(dbPath, backupDir, DateTime.UtcNow)) return;

            string backupFileName = $"library-{UtcTime.FileStamp()}.db";
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
            .Max(f => File.GetLastWriteTimeUtc(f));

        return lastChange > newest.Value;
    }

    internal static DateTime? TryParseBackupTime(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        const string prefix = "library-";
        if (!name.StartsWith(prefix, StringComparison.Ordinal)) return null;
        
        return DateTime.TryParseExact(name[prefix.Length..], "yyyyMMdd-HHmmss",
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var time)
            ? time
            : null;
    }

    private void PruneOldBackups(string backupDir, int retentionCount)
    {
        try
        {
            var backupFiles = Directory.GetFiles(backupDir, "library-*.db")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.Name, StringComparer.Ordinal)
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

    public void Vacuum()
    {
        lock (_dbLock)
        {
            var sizeBefore = new FileInfo(DbPath).Length;
            _db.Execute("VACUUM;");
            var sizeAfter = new FileInfo(DbPath).Length;
            Log.Information("[DatabaseService] VACUUM complete: {Before}KB -> {After}KB", sizeBefore / 1024, sizeAfter / 1024);

            if (_ftsAvailable)
            {
                try { _db.Execute("INSERT INTO TracksFts(TracksFts) VALUES('rebuild');"); }
                catch (Exception ex) { Log.Warning(ex, "[DatabaseService] FTS rebuild after VACUUM failed"); }
            }
        }
    }

    public void BackupToPath(string targetPath)
    {
        FlushWrites();
        lock (_dbLock)
        {
            if (File.Exists(targetPath)) File.Delete(targetPath);
            _db.Execute($"VACUUM INTO '{targetPath.Replace("'", "''")}';");
            if (!IsValidDatabase(targetPath))
            {
                try { File.Delete(targetPath); } catch { }
                throw new InvalidOperationException("Backup validation failed.");
            }
        }
    }
}