using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NullWave.Models;
using NullWave.Services;

namespace NullWave.ViewModels;

public partial class SmartPlaylistViewModel : ObservableObject
{
    private readonly SmartPlaylistService _service;
    private readonly SmartPlaylistDefinition _definition;
    private readonly PlaylistService _playlistService;

    [ObservableProperty] private IReadOnlyList<Track> _tracks = new List<Track>();

    public string NameKey => _definition.NameKey;
    public string Id => _definition.Id;

    // Constructor now takes PlaylistService
    public SmartPlaylistViewModel(SmartPlaylistService service, SmartPlaylistDefinition definition, PlaylistService playlistService)
    {
        _service = service;
        _definition = definition;
        _playlistService = playlistService;
        Refresh();
    }

    public void Refresh() => Tracks = _service.Resolve(_definition);

    [RelayCommand]
    private void PlayAll() { /* TODO: Wire to PlayerViewModel.PlayAll(Tracks) */ }

    [RelayCommand]
    private void Shuffle() { /* TODO: Wire to PlayerViewModel.ShufflePlay(Tracks) */ }

    // IMPLEMENTED: Create snapshot playlist
    [RelayCommand]
    private async Task SaveAsPlaylist()
    {
        if (Tracks.Count == 0)
        {
            ToastService.Instance.Show("Cannot save empty smart playlist", ToastType.Warning, scope: "playlist");
            return;
        }

        var snapshotName = $"Smart: {_definition.NameKey} (Snapshot)";
        
        // Ensure unique name
        var baseName = snapshotName;
        int counter = 1;
        while (_playlistService.NameExists(snapshotName))
        {
            snapshotName = $"{baseName} ({counter++})";
        }

        var playlist = _playlistService.Create(snapshotName, $"Auto-generated snapshot from smart playlist '{_definition.NameKey}'");
        
        foreach (var track in Tracks)
        {
            _playlistService.AddTrack(playlist.Id, track);
        }

        ToastService.Instance.Show($"Saved as '{snapshotName}' with {Tracks.Count} tracks", ToastType.Success, scope: "playlist");
    }
}