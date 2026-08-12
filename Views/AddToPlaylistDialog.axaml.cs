using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using NullWave.Models;
using NullWave.Services;

namespace NullWave.Views;

public partial class AddToPlaylistDialog : Window
{
    private readonly PlaylistService? _playlists;
    private readonly ObservableCollection<Playlist> _filtered = new();

    public AddToPlaylistDialog() { InitializeComponent(); }

    public AddToPlaylistDialog(PlaylistService playlists) : this()
    {
        _playlists = playlists;
        PlaylistList.ItemsSource = _filtered;
        Refresh("");
    }

    private void Refresh(string query)
    {
        if (_playlists == null) return;
        _filtered.Clear();
        foreach (var p in _playlists.GetAll()
                     .Where(p => string.IsNullOrWhiteSpace(query) || p.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            _filtered.Add(p);
    }

    private void OnSearchChanged(object? sender, TextChangedEventArgs e) => Refresh(SearchBox.Text ?? "");

    private async void OnNewPlaylist(object? sender, RoutedEventArgs e)
    {
        var name = await new CreatePlaylistDialog().ShowDialog<string?>(this);
        if (string.IsNullOrWhiteSpace(name) || _playlists == null) return;
        if (_playlists.NameExists(name)) name = $"{name} ({Guid.NewGuid().ToString()[..4]})";
        var created = _playlists.Create(name);
        Refresh(SearchBox.Text ?? "");
        PlaylistList.SelectedItem = created;
    }

    private void OnOk(object? sender, RoutedEventArgs e) => Close(PlaylistList.SelectedItem as Playlist);
    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}