using System;
using System.Collections.Generic;
using System.Linq;
using NullWave.Models;

namespace NullWave.Services;

public class PlaylistService
{
    private readonly List<Playlist> _playlists = new();
    private readonly List<PlaylistFolder> _folders = new();
    private readonly DatabaseService _db;

    public PlaylistService(DatabaseService db, LibraryService library)
    {
        _db = db;
        _playlists = _db.LoadPlaylists(library.GetAll().ToList());
        // Note: Ensure your DatabaseService has LoadPlaylistFolders() implemented, 
        // or initialize _folders from your DB accordingly.
        _folders = _db.LoadPlaylistFolders(); 
    }

    public IReadOnlyList<Playlist> GetAll() => _playlists.AsReadOnly();
    public IReadOnlyList<PlaylistFolder> GetAllFolders() => _folders.AsReadOnly();

    public Playlist Create(string name, string? description = null, Guid? folderId = null)
    {
        var playlist = new Playlist { Name = name, Description = description, FolderId = folderId };
        _playlists.Add(playlist);
        _db.SavePlaylist(playlist);
        return playlist;
    }

    public PlaylistFolder CreateFolder(string name)
    {
        var folder = new PlaylistFolder { Name = name };
        _folders.Add(folder);
        _db.SavePlaylistFolder(folder);
        return folder;
    }

    public void Remove(Guid id)
    {
        var playlist = _playlists.FirstOrDefault(p => p.Id == id);
        if (playlist != null) 
        {
            _playlists.Remove(playlist);
            _db.DeletePlaylist(id);
        }
    }

    public void RemoveFolder(Guid id)
    {
        var folder = _folders.FirstOrDefault(f => f.Id == id);
        if (folder != null)
        {
            _folders.Remove(folder);
            _db.DeletePlaylistFolder(id);
            
            // Unlink playlists in this folder rather than deleting them
            foreach (var playlist in _playlists.Where(p => p.FolderId == id).ToList())
            {
                playlist.FolderId = null;
                _db.SavePlaylist(playlist);
            }
        }
    }

    public Playlist? GetById(Guid id) => _playlists.FirstOrDefault(p => p.Id == id);
    public PlaylistFolder? GetFolderById(Guid id) => _folders.FirstOrDefault(f => f.Id == id);

    public bool AddTrack(Guid playlistId, Track track)
    {
        var playlist = GetById(playlistId);
        if (playlist == null) return false;
        if (playlist.Tracks.Any(t => t.Id == track.Id)) return false;

        playlist.Tracks.Add(track);
        _db.SavePlaylist(playlist);
        return true;
    }

    public bool RemoveTrack(Guid playlistId, Guid trackId)
    {
        var playlist = GetById(playlistId);
        if (playlist == null) return false;

        var track = playlist.Tracks.FirstOrDefault(t => t.Id == trackId);
        if (track == null) return false;

        playlist.Tracks.Remove(track);
        _db.SavePlaylist(playlist);
        return true;
    }

    public bool MoveTrack(Guid playlistId, int fromIndex, int toIndex)
    {
        var playlist = GetById(playlistId);
        if (playlist == null) return false;
        if (fromIndex < 0 || toIndex < 0) return false;
        if (fromIndex >= playlist.Tracks.Count || toIndex >= playlist.Tracks.Count) return false;

        var track = playlist.Tracks[fromIndex];
        playlist.Tracks.RemoveAt(fromIndex);
        playlist.Tracks.Insert(toIndex, track);
        
        _db.SavePlaylist(playlist);
        return true;
    }

    public bool Rename(Guid id, string newName)
    {
        var playlist = GetById(id);
        if (playlist == null) return false;
        playlist.Name = newName;
        _db.SavePlaylist(playlist);
        return true;
    }

    public bool RenameFolder(Guid id, string newName)
    {
        var folder = GetFolderById(id);
        if (folder == null) return false;
        folder.Name = newName;
        _db.SavePlaylistFolder(folder);
        return true;
    }

    public void MovePlaylistToFolder(Guid playlistId, Guid? folderId)
    {
        var playlist = GetById(playlistId);
        if (playlist != null)
        {
            playlist.FolderId = folderId;
            _db.SavePlaylist(playlist);
        }
    }

    public int GetTrackCount(Guid id) => GetById(id)?.Tracks.Count ?? 0;

    public bool NameExists(string name) =>
        _playlists.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}