using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using NullWave.Helpers;
using NullWave.Models;
using NullWave.Services;
using NullWave.ViewModels.Base;

namespace NullWave.ViewModels;

public class PlaylistViewModel : ViewModelBase
{
    private readonly PlaylistService _playlists;
    private Playlist? _selectedPlaylist;

    public ObservableCollection<Playlist> Playlists { get; } = new();
    public ICommand CreatePlaylistCommand { get; }
    public ICommand RemovePlaylistCommand { get; }
    public ICommand AddToPlaylistCommand { get; }
    public ICommand RemoveFromPlaylistCommand { get; }

    public Playlist? SelectedPlaylist
    {
        get => _selectedPlaylist;
        set { _selectedPlaylist = value; OnPropertyChanged(); }
    }

    public PlaylistViewModel(PlaylistService playlists)
    {
        _playlists = playlists;
        CreatePlaylistCommand = new RelayCommand(CreatePlaylist);
        RemovePlaylistCommand = new RelayCommand(RemovePlaylist);
        AddToPlaylistCommand = new RelayCommand<Track>(AddToPlaylist);
        RemoveFromPlaylistCommand = new RelayCommand<Track>(RemoveFromPlaylist);
    }

    private void CreatePlaylist()
    {
        var name = $"Playlist {Playlists.Count + 1}";
        if (_playlists.NameExists(name))
            name = $"Playlist {Guid.NewGuid().ToString()[..4]}";

        var playlist = _playlists.Create(name);
        Playlists.Add(playlist);
        SelectedPlaylist = playlist;
    }

    private void RemovePlaylist()
    {
        if (SelectedPlaylist == null) return;
        _playlists.Remove(SelectedPlaylist.Id);
        Playlists.Remove(SelectedPlaylist);
        SelectedPlaylist = null;
    }

    private void AddToPlaylist(Track? track)
    {
        if (track == null || SelectedPlaylist == null) return;
        _playlists.AddTrack(SelectedPlaylist.Id, track);
    }

    private void RemoveFromPlaylist(Track? track)
    {
        if (track == null || SelectedPlaylist == null) return;
        _playlists.RemoveTrack(SelectedPlaylist.Id, track.Id);
    }
}