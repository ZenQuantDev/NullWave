using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NullWave.Helpers;
using NullWave.Models;
using SQLite;
using Serilog;

namespace NullWave.Services;

public class DatabaseService : IDisposable
{
    private readonly SQLiteAsyncConnection _db;
    private bool _disposed;

    public DatabaseService()
    {
        var path = NullWavePaths.DatabasePath;
        _db = new SQLiteAsyncConnection(path);
        
        // Table initializations run at setup
        _db.CreateTableAsync<TrackRecord>().Wait();
        _db.CreateTableAsync<PlaylistRecord>().Wait();
        _db.CreateTableAsync<PlaylistTrackRecord>().Wait();
        
        Log.Information("[DatabaseService] Opened DB at {Path}", path);
    }

    // ==========================================
    // ASYNC API (For PlaybackNavigator / New Code)
    // ==========================================

    public async Task<List<Track>> LoadAllAsync()
    {
        try
        {
            var records = await _db.Table<TrackRecord>().ToListAsync();
            return records.Select(r => r.ToTrack()).ToList();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[DatabaseService] Failed to load tracks");
            return new List<Track>();
        }
    }

    public async Task InsertAsync(Track track)
    {
        try 
        { 
            await _db.InsertOrReplaceAsync(TrackRecord.FromTrack(track)); 
        }
        catch (Exception ex) 
        { 
            Log.Error(ex, "[DatabaseService] Insert failed for {Title}", track.Title); 
        }
    }

    public async Task UpdateAsync(Track track)
    {
        try 
        { 
            await _db.InsertOrReplaceAsync(TrackRecord.FromTrack(track)); 
        }
        catch (Exception ex) 
        { 
            Log.Error(ex, "[DatabaseService] Update failed for {Title}", track.Title); 
        }
    }

    public async Task DeleteAsync(Guid id)
    {
        try 
        { 
            await _db.DeleteAsync<TrackRecord>(id.ToString()); 
        }
        catch (Exception ex) 
        { 
            Log.Error(ex, "[DatabaseService] Delete failed for {Id}", id); 
        }
    }

    public async Task SavePlaylistAsync(Playlist playlist)
    {
        try
        {
            await _db.RunInTransactionAsync(tran =>
            {
                tran.InsertOrReplace(new PlaylistRecord 
                { 
                    Id = playlist.Id.ToString(), 
                    Name = playlist.Name, 
                    Description = playlist.Description 
                });

                tran.Execute("DELETE FROM PlaylistTracks WHERE PlaylistId = ?", playlist.Id.ToString());
                
                for (int i = 0; i < playlist.Tracks.Count; i++)
                {
                    tran.Insert(new PlaylistTrackRecord 
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

    public async Task<List<Playlist>> LoadPlaylistsAsync(List<Track> entireLibrary)
    {
        var playlists = new List<Playlist>();
        try
        {
            var trackMap = entireLibrary.ToDictionary(t => t.Id.ToString());

            var dbPlaylists = await _db.Table<PlaylistRecord>().ToListAsync();
            foreach (var plRecord in dbPlaylists)
            {
                var pList = new Playlist 
                { 
                    Id = Guid.Parse(plRecord.Id), 
                    Name = plRecord.Name, 
                    Description = plRecord.Description 
                };

                var dbTracks = await _db.Table<PlaylistTrackRecord>()
                                      .Where(pt => pt.PlaylistId == plRecord.Id)
                                      .OrderBy(pt => pt.SortOrder)
                                      .ToListAsync();

                foreach (var pt in dbTracks)
                {
                    if (trackMap.TryGetValue(pt.TrackId, out var track))
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

    public async Task DeletePlaylistAsync(Guid id)
    {
        try
        {
            var strId = id.ToString();
            await _db.DeleteAsync<PlaylistRecord>(strId);
            await _db.ExecuteAsync("DELETE FROM PlaylistTracks WHERE PlaylistId = ?", strId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to delete playlist from database.");
        }
    }

    // ==========================================
    // SYNCHRONOUS COMPLIANCE BRIDGE (For Services)
    // ==========================================

    public List<Track> LoadAll()
    {
        try
        {
            var records = _db.GetConnection().Table<TrackRecord>().ToList();
            return records.Select(r => r.ToTrack()).ToList();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[DatabaseService] Failed to load tracks");
            return new List<Track>();
        }
    }

    public void Insert(Track track)
    {
        try 
        { 
            _db.GetConnection().InsertOrReplace(TrackRecord.FromTrack(track)); 
        }
        catch (Exception ex) 
        { 
            Log.Error(ex, "[DatabaseService] Insert failed for {Title}", track.Title); 
        }
    }

    public void Update(Track track)
    {
        try 
        { 
            _db.GetConnection().InsertOrReplace(TrackRecord.FromTrack(track)); 
        }
        catch (Exception ex) 
        { 
            Log.Error(ex, "[DatabaseService] Update failed for {Title}", track.Title); 
        }
    }

    public void Delete(Guid id)
    {
        try 
        { 
            _db.GetConnection().Delete<TrackRecord>(id.ToString()); 
        }
        catch (Exception ex) 
        { 
            Log.Error(ex, "[DatabaseService] Delete failed for {Id}", id); 
        }
    }

    public void RunInTransaction(Action action)
    {
        try
        {
            _db.GetConnection().RunInTransaction(action);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[DatabaseService] Transaction failed");
        }
    }

    public void SavePlaylist(Playlist playlist)
    {
        try
        {
            var syncDb = _db.GetConnection();
            syncDb.RunInTransaction(() =>
            {
                syncDb.InsertOrReplace(new PlaylistRecord 
                { 
                    Id = playlist.Id.ToString(), 
                    Name = playlist.Name, 
                    Description = playlist.Description 
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
            var syncDb = _db.GetConnection();

            var dbPlaylists = syncDb.Table<PlaylistRecord>().ToList();
            foreach (var plRecord in dbPlaylists)
            {
                var pList = new Playlist 
                { 
                    Id = Guid.Parse(plRecord.Id), 
                    Name = plRecord.Name, 
                    Description = plRecord.Description 
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
            var syncDb = _db.GetConnection();
            syncDb.Delete<PlaylistRecord>(strId);
            syncDb.Execute("DELETE FROM PlaylistTracks WHERE PlaylistId = ?", strId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to delete playlist from database.");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}
