using System;
using System.Collections.Generic;
using System.Linq;
using SQLite;
using Serilog;

namespace NullWave.Services;

public partial class DatabaseService
{
    private void EnsureFts5Index()
    {
        lock (_dbLock)
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
        lock (_dbLock)
        {
            return _db.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='TracksFts';") > 0;
        }
    }

    public HashSet<Guid>? SearchFts(string query)
    {
        if (!_ftsAvailable) return null;
        var matchQuery = BuildFtsQuery(query);
        if (string.IsNullOrEmpty(matchQuery)) return null;

        lock (_dbLock)
        {
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
    }

    private static string BuildFtsQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return string.Empty;
        
        // FIX: Normalize to FormC so decomposed accents (e + ´) become single characters before we filter
        query = query.Normalize(System.Text.NormalizationForm.FormC);

        var words = new string(query.Select(character => char.IsLetterOrDigit(character) ? character : ' ').ToArray())
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var terms = words.Select(term => $"\"{term}\"*");

        return string.Join(" ", terms);
    }

    public void RebuildFtsIndex()
    {
        lock (_dbLock)
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
    }
}