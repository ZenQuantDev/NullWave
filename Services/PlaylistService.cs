using System;
using System.Collections.Generic;
using System.Linq;
using NullWave.Models;

namespace NullWave.Services;

public class PlaylistService
{
    private readonly List<Playlist> _playlists = new();

    // ── Core ──────────────────────────────────────────
    public IReadOnlyList<Playlist> GetAll() => _playlists.AsReadOnly();

    public Playlist Create(string name, string? description = null)
    {
        var playlist = new Playlist
        {
            Name = name,
            Description = description
        };
        _playlists.Add(playlist);
        return playlist;
    }

    public void Remove(Guid id)
    {
        var playlist = _playlists.FirstOrDefault(p => p.Id == id);
        if (playlist != null) _playlists.Remove(playlist);
    }

    public Playlist? GetById(Guid id) =>
        _playlists.FirstOrDefault(p => p.Id == id);

    // ── Track Management ──────────────────────────────
    public bool AddTrack(Guid playlistId, Track track)
    {
        var playlist = GetById(playlistId);
        if (playlist == null) return false;
        if (playlist.Tracks.Any(t => t.Id == track.Id)) return false;

        playlist.Tracks.Add(track);
        return true;
    }

    public bool RemoveTrack(Guid playlistId, Guid trackId)
    {
        var playlist = GetById(playlistId);
        if (playlist == null) return false;

        var track = playlist.Tracks.FirstOrDefault(t => t.Id == trackId);
        if (track == null) return false;

        playlist.Tracks.Remove(track);
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
        return true;
    }

    // ── Rename ────────────────────────────────────────
    public bool Rename(Guid id, string newName)
    {
        var playlist = GetById(id);
        if (playlist == null) return false;
        playlist.Name = newName;
        return true;
    }

    // ── Stats ─────────────────────────────────────────
    public int GetTrackCount(Guid id) => GetById(id)?.Tracks.Count ?? 0;

    public bool NameExists(string name) =>
        _playlists.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}