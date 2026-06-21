using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using NullWave.Helpers;
using NullWave.Models;
using NullWave.Services;
using NullWave.ViewModels.Base;
using Serilog;

namespace NullWave.ViewModels;

public class PlaylistViewModel : ViewModelBase
{
    private readonly PlaylistService _playlists;
    private Playlist? _selectedPlaylist;
    private string _renameText = string.Empty;
    private bool _isRenaming;

    public ObservableCollection<Playlist> Playlists { get; } = new();

    public ICommand CreatePlaylistCommand { get; }
    public ICommand RemovePlaylistCommand { get; }
    public ICommand AddToPlaylistCommand { get; }
    public ICommand RemoveFromPlaylistCommand { get; }
    public ICommand StartRenameCommand { get; }
    public ICommand ConfirmRenameCommand { get; }
    public ICommand CancelRenameCommand { get; }

    public Playlist? SelectedPlaylist
    {
        get => _selectedPlaylist;
        set { _selectedPlaylist = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Bound to an inline rename TextBox in PlaylistsView. Separate from
    /// SelectedPlaylist.Name so typing doesn't mutate the real name until
    /// the user confirms via ConfirmRenameCommand.
    /// </summary>
    public string RenameText
    {
        get => _renameText;
        set { _renameText = value; OnPropertyChanged(); }
    }

    public bool IsRenaming
    {
        get => _isRenaming;
        set { _isRenaming = value; OnPropertyChanged(); }
    }

    public PlaylistViewModel(PlaylistService playlists)
    {
        _playlists = playlists;
        CreatePlaylistCommand = new RelayCommand(async () => await CreatePlaylistAsync());
        RemovePlaylistCommand = new RelayCommand(RemovePlaylist);
        AddToPlaylistCommand = new RelayCommand<Track>(AddToPlaylist);
        RemoveFromPlaylistCommand = new RelayCommand<Track>(RemoveFromPlaylist);
        StartRenameCommand = new RelayCommand(StartRename);
        ConfirmRenameCommand = new RelayCommand(ConfirmRename);
        CancelRenameCommand = new RelayCommand(() => IsRenaming = false);

        Refresh();
    }

    /// <summary>
    /// Re-syncs the observable collection from PlaylistService.GetAll().
    /// Call this after any playlist is created/modified outside this
    /// ViewModel (e.g. MoodPlaylistService creating a playlist directly
    /// via PlaylistService) so the UI reflects the change.
    /// </summary>
    public void Refresh()
    {
        var selectedId = SelectedPlaylist?.Id;

        Playlists.Clear();
        foreach (var playlist in _playlists.GetAll())
            Playlists.Add(playlist);

        SelectedPlaylist = selectedId.HasValue
            ? Playlists.FirstOrDefault(p => p.Id == selectedId.Value)
            : null;
    }

    private async Task CreatePlaylistAsync()
    {
        var window = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow : null;
        if (window == null) return;

        var dialog = new Views.CreatePlaylistDialog();
        var name = await dialog.ShowDialog<string?>(window);

        if (string.IsNullOrWhiteSpace(name)) return;

        if (_playlists.NameExists(name))
            name = $"{name} ({Guid.NewGuid().ToString()[..4]})";

        var playlist = _playlists.Create(name);
        Playlists.Add(playlist);
        SelectedPlaylist = playlist;
        Log.Information("[Playlist] Created: {Name}", name);
    }

    private void RemovePlaylist()
    {
        if (SelectedPlaylist == null) return;
        _playlists.Remove(SelectedPlaylist.Id);
        Playlists.Remove(SelectedPlaylist);
        SelectedPlaylist = null;
    }

    private void StartRename()
    {
        if (SelectedPlaylist == null) return;
        RenameText = SelectedPlaylist.Name;
        IsRenaming = true;
    }

    private void ConfirmRename()
    {
        if (SelectedPlaylist == null) return;

        var trimmed = RenameText.Trim();
        if (!string.IsNullOrWhiteSpace(trimmed))
        {
            _playlists.Rename(SelectedPlaylist.Id, trimmed);
            // Playlist is a reference type held in both _playlists and
            // Playlists collection, but Rename() may not raise property
            // change on its own — force a refresh so the UI updates.
            Refresh();
            SelectedPlaylist = Playlists.FirstOrDefault(p => p.Name == trimmed) ?? SelectedPlaylist;
        }

        IsRenaming = false;
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