using System;
using System.Collections.Generic;
using System.Linq;
using NullWave.Helpers;
using NullWave.Models;
using SQLite;
using Serilog;

namespace NullWave.Services;

public partial class DatabaseService
{
    public void SavePlaylist(Playlist playlist)
    {
        lock (_dbLock)
        {
            try
            {
                var record = PlaylistRecord.FromPlaylist(playlist);

                _db.RunInTransaction(() =>
                {
                    _db.InsertOrReplace(record);
                    _db.Execute("DELETE FROM PlaylistTracks WHERE PlaylistId = ?", record.Id);

                    int sortOrder = 0;
                    foreach (var track in playlist.Tracks)
                    {
                        _db.Insert(new PlaylistTrackRecord
                        {
                            PlaylistId = record.Id,
                            TrackId = track.Id.ToString(),
                            SortOrder = sortOrder++,
                            DateAdded = playlist.GetDateAdded(track.Id)
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[DatabaseService] Failed to save playlist {Name}", playlist.Name);
            }
        }
    }

    public List<Playlist> LoadPlaylists(List<Track> entireLibrary)
    {
        var playlists = new List<Playlist>();
        lock (_dbLock)
        {
            try
            {
                var trackMap = entireLibrary.ToDictionary(t => t.Id.ToString());

                foreach (var plRecord in _db.Table<PlaylistRecord>().ToList())
                {
                    var playlist = plRecord.ToPlaylist();

                    var ptRecords = _db.Table<PlaylistTrackRecord>()
                        .Where(pt => pt.PlaylistId == plRecord.Id)
                        .OrderBy(pt => pt.SortOrder)
                        .ToList();

                    foreach (var pt in ptRecords)
                    {
                        if (trackMap.TryGetValue(pt.TrackId, out var track))
                        {
                            playlist.Tracks.Add(track);
                            playlist.TrackDateAdded[track.Id] = pt.DateAdded == default 
                                ? playlist.DateCreated 
                                : pt.DateAdded;
                        }
                    }

                    playlists.Add(playlist);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[DatabaseService] Failed to load playlists from database.");
            }
        }
        return playlists;
    }

    public void SavePlaylistFolder(PlaylistFolder folder)
    {
        lock (_dbLock)
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
    }

    public List<PlaylistFolder> LoadPlaylistFolders()
    {
        var folders = new List<PlaylistFolder>();
        lock (_dbLock)
        {
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
        }
        return folders;
    }

    public void DeletePlaylistFolder(Guid id)
    {
        lock (_dbLock)
        {
            try { _db.Delete<PlaylistFolderRecord>(id.ToString()); }
            catch (Exception ex) { Log.Error(ex, "Failed to delete playlist folder from database."); }
        }
    }

    public void DeletePlaylist(Guid id)
    {
        lock (_dbLock)
        {
            try
            {
                var strId = id.ToString();
                _db.Delete<PlaylistRecord>(strId);
                _db.Execute("DELETE FROM PlaylistTracks WHERE PlaylistId = ?", strId);
            }
            catch (Exception ex) { Log.Error(ex, "Failed to delete playlist from database."); }
        }
    }
}