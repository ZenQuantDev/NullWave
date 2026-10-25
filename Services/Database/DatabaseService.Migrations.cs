using System;
using System.Linq;
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

        lock (_dbLock)
        {
            try
            {
                _db.RunInTransaction(() =>
                {
                    int ver = _db.ExecuteScalar<int>("PRAGMA user_version;");

                    if (ver < 9)
                    {
                        foreach (var r in _db.Table<TrackRecord>().ToList())
                        {
                            var f = PathHelper.Tokenize(r.FilePath);
                            var a = PathHelper.Tokenize(r.AlbumArtPath);
                            if (f == r.FilePath && a == r.AlbumArtPath) continue;
                            r.FilePath = f;
                            r.AlbumArtPath = a;
                            SaveTrackRecord(r);
                        }
                        foreach (var p in _db.Table<PlaylistRecord>().ToList())
                        {
                            var art = PathHelper.Tokenize(p.CustomArtPath);
                            if (art == p.CustomArtPath) continue;
                            p.CustomArtPath = art;
                            _db.InsertOrReplace(p);
                        }
                        _db.Execute("PRAGMA user_version = 9;");
                        ver = 9;
                        Log.Information("[DatabaseService] Migration 9: paths tokenized");
                    }

                    if (ver < 10)
                    {
                        int converted = 0;
                        foreach (var r in _db.Table<TrackRecord>().ToList())
                        {
                            if (!r.LastPlayed.HasValue) continue;
                            r.LastPlayed = LocalToUtc(r.LastPlayed.Value);
                            _db.Update(r);
                            converted++;
                        }
                        _db.Execute("PRAGMA user_version = 10;");
                        Log.Information("[DatabaseService] Migration 10: {Count} LastPlayed values local->UTC", converted);
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[DatabaseService] Data migration failed and was rolled back");
            }
        }
    }

    private static DateTime LocalToUtc(DateTime stored)
    {
        var local = DateTime.SpecifyKind(stored, DateTimeKind.Unspecified);
        if (TimeZoneInfo.Local.IsInvalidTime(local)) local = local.AddHours(1);
        return DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeToUtc(local, TimeZoneInfo.Local), DateTimeKind.Utc);
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