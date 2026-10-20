using System;
using System.Linq;
using System.Threading.Tasks;
using NullWave.Helpers;
using NullWave.Models;
using SQLite;
using Serilog;

namespace NullWave.Services;

public partial class DatabaseService
{
    private void MigrateSchema()
    {
        lock (_dbLock)
        {
            try
            {
                int v = _db.ExecuteScalar<int>("PRAGMA user_version;");
                if (v == 0)
                {
                    v = DetectCurrentVersion();
                    if (v > 0)
                    {
                        _db.Execute($"PRAGMA user_version = {v};");
                        Log.Information("[DatabaseService] Detected existing legacy schema at version {Version}", v);
                    }
                }

                void Step(int target, string sql)
                {
                    if (v >= target) return;
                    _db.Execute(sql);
                    v = target;
                    _db.Execute($"PRAGMA user_version = {target};");
                }

                Step(1, "ALTER TABLE Playlists ADD COLUMN CustomArtPath VARCHAR;");
                Step(2, "ALTER TABLE Tracks ADD COLUMN DurationMs INTEGER DEFAULT 0;");
                Step(3, "ALTER TABLE Tracks ADD COLUMN MediaType VARCHAR DEFAULT 'Music';");
                Step(4, "ALTER TABLE Tracks ADD COLUMN PlaybackPositionTicks INTEGER DEFAULT 0;");
                Step(5, "ALTER TABLE Tracks ADD COLUMN Album VARCHAR;");
                Step(6, "ALTER TABLE Tracks ADD COLUMN TrackNumber INTEGER DEFAULT 0;");
                Step(7, "ALTER TABLE PlaylistTracks ADD COLUMN DateAdded DATETIME;");

                if (v < 8)
                {
                    var repaired = _db.Execute("UPDATE Tracks SET MediaType = 'Music' WHERE MediaType = 'Radio' AND (FilePath IS NOT NULL OR Source IN ('YouTube', 'SoundCloud', 'Local', 'Spotify', 'LastFm'));");
                    if (repaired > 0) Log.Information("[DatabaseService] Migration 8: repaired {Count} misclassified Radio tracks", repaired);
                    v = 8;
                    _db.Execute("PRAGMA user_version = 8;");
                }

                Log.Information("[DatabaseService] Schema is at version {Version}", v);
            }
            catch (Exception ex) { Log.Warning(ex, "[DatabaseService] Schema migration check failed"); }
        }

        var token = _migrationCts.Token;
        _ = Task.Run(() =>
        {
            try
            {
                lock (_dbLock)
                {
                    if (token.IsCancellationRequested || _disposed) return;
                    if (_db.ExecuteScalar<int>("PRAGMA user_version;") >= 9) return;

                    bool dbUpdated = false;
                    foreach (var r in _db.Table<TrackRecord>().ToList())
                    {
                        if (token.IsCancellationRequested) return;
                        bool changed = false;
                        var tokFilePath = PathHelper.Tokenize(r.FilePath);
                        if (tokFilePath != r.FilePath) { r.FilePath = tokFilePath; changed = true; }
                        var tokArtPath = PathHelper.Tokenize(r.AlbumArtPath);
                        if (tokArtPath != r.AlbumArtPath) { r.AlbumArtPath = tokArtPath; changed = true; }
                        if (changed) { SaveTrackRecord(r); dbUpdated = true; }
                    }

                    foreach (var p in _db.Table<PlaylistRecord>().ToList())
                    {
                        if (token.IsCancellationRequested) return;
                        var tokArt = PathHelper.Tokenize(p.CustomArtPath);
                        if (tokArt != p.CustomArtPath) { p.CustomArtPath = tokArt; _db.InsertOrReplace(p); dbUpdated = true; }
                    }

                    if (dbUpdated) Log.Information("[DatabaseService] Migrated absolute paths to portable tokens.");
                    _db.Execute("PRAGMA user_version = 9;");
                }
            }
            catch (Exception ex) { Log.Warning(ex, "[DatabaseService] Path tokenization migration failed"); }
        });
    }

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
}