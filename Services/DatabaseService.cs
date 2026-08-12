using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NullWave.Helpers;
using NullWave.Models;
using SQLite;
using Serilog;

namespace NullWave.Services;

public class DatabaseService : IDisposable
{
    private readonly SQLiteConnection _db;
    private bool _disposed;
    public string DbPath { get; }

    public DatabaseService()
    {
        var path = NullWavePaths.DatabasePath;
        DbPath = path;
        _db = new SQLiteConnection(path);

        _db.CreateTable<TrackRecord>();
        _db.CreateTable<PlaylistRecord>();
        _db.CreateTable<PlaylistTrackRecord>();
        _db.CreateTable<PlaylistFolderRecord>();

        MigrateSchema();

        Log.Information("[DatabaseService] Opened DB at {Path}", path);
    }

    /// <summary>SQLite-net CreateTable does NOT add columns to existing tables; add new columns manually.</summary>
    private void MigrateSchema()
    {
        try
        {
            var cols = _db.Query<ColumnInfo>("PRAGMA table_info(Playlists);");
            if (!cols.Any(c => string.Equals(c.Name, "CustomArtPath", StringComparison.OrdinalIgnoreCase)))
            {
                _db.Execute("ALTER TABLE Playlists ADD COLUMN CustomArtPath VARCHAR;");
                Log.Information("[DatabaseService] Migration: added Playlists.CustomArtPath column");
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[DatabaseService] Schema migration check failed");
        }
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
        try { _db.InsertOrReplace(TrackRecord.FromTrack(track)); }
        catch (Exception ex) { Log.Error(ex, "[DatabaseService] Insert failed for {Title}", track.Title); }
    }

    public void Update(Track track)
    {
        try { _db.InsertOrReplace(TrackRecord.FromTrack(track)); }
        catch (Exception ex) { Log.Error(ex, "[DatabaseService] Update failed for {Title}", track.Title); }
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
                syncDb.InsertOrReplace(new PlaylistRecord
                {
                    Id = playlist.Id.ToString(),
                    Name = playlist.Name,
                    Description = playlist.Description,
                    FolderId = playlist.FolderId?.ToString(),
                    CustomArtPath = playlist.CustomArtPath
                });
                syncDb.Execute("DELETE FROM PlaylistTracks WHERE PlaylistId = ?", playlist.Id.ToString());

                for (int i = 0; i < playlist.Tracks.Count; i++)
                {
                    syncDb.Insert(new PlaylistTrackRecord
                    {
                        PlaylistId = playlist.Id.ToString(),
                        TrackId = playlist.Tracks[i].Id.ToString(),
                        SortOrder = i
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
                    if (trackMap.TryGetValue(pt.TrackId, out var track))
                        pList.Tracks.Add(track);

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
        _db.Close();
    }
}