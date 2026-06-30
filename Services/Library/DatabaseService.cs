using System;
using System.Collections.Generic;
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

    public DatabaseService()
    {
        var path = NullWavePaths.DatabasePath;
        _db = new SQLiteConnection(path);
        _db.CreateTable<TrackRecord>();
        _db.CreateTable<PlaylistRecord>();
        _db.CreateTable<PlaylistTrackRecord>();
        Log.Information("[DatabaseService] Opened DB at {Path}", path);
    }

    public List<Track> LoadAll()
    {
        try
        {
            return _db.Table<TrackRecord>()
                      .ToList()
                      .Select(r => r.ToTrack())
                      .ToList();
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

    public void SavePlaylist(Playlist playlist)
    {
        try
        {
            _db.InsertOrReplace(new PlaylistRecord 
            { 
                Id = playlist.Id.ToString(), 
                Name = playlist.Name, 
                Description = playlist.Description 
            });

            _db.Execute("DELETE FROM PlaylistTracks WHERE PlaylistId = ?", playlist.Id.ToString());
            
            for (int i = 0; i < playlist.Tracks.Count; i++)
            {
                _db.Insert(new PlaylistTrackRecord 
                { 
                    PlaylistId = playlist.Id.ToString(), 
                    TrackId = playlist.Tracks[i].Id.ToString(), 
                    SortOrder = i 
                });
            }
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
            var dbPlaylists = _db.Table<PlaylistRecord>().ToList();
            foreach (var plRecord in dbPlaylists)
            {
                var pList = new Playlist 
                { 
                    Id = Guid.Parse(plRecord.Id), 
                    Name = plRecord.Name, 
                    Description = plRecord.Description 
                };

                var dbTracks = _db.Table<PlaylistTrackRecord>()
                                  .Where(pt => pt.PlaylistId == plRecord.Id)
                                  .OrderBy(pt => pt.SortOrder)
                                  .ToList();

                foreach (var pt in dbTracks)
                {
                    var track = entireLibrary.FirstOrDefault(t => t.Id.ToString() == pt.TrackId);
                    if (track != null)
                    {
                        pList.Tracks.Add(track);
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

    public void DeletePlaylist(Guid id)
    {
        try
        {
            var strId = id.ToString();
            _db.Delete<PlaylistRecord>(strId);
            _db.Execute("DELETE FROM PlaylistTracks WHERE PlaylistId = ?", strId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to delete playlist from database.");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _db.Close();
        _db.Dispose();
        _disposed = true;
    }
}