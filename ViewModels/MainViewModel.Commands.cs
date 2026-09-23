using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using NullWave.Helpers;
using NullWave.Helpers.Logging;
using NullWave.Models;
using NullWave.Services;
using Serilog;

namespace NullWave.ViewModels;

public partial class MainViewModel
{
    private void InitializeCommands()
    {
        ToggleCustomizeSidebarCommand = new RelayCommand(() => IsCustomizingSidebar = !IsCustomizingSidebar);
        ToggleSidebarCollapsedCommand = new RelayCommand(() =>
        {
            IsSidebarCollapsed = !IsSidebarCollapsed;
            _prefsService.Update(p => p.SidebarCollapsed = IsSidebarCollapsed);
        });
        ClearGlobalSearchCommand = new RelayCommand(() => GlobalSearchQuery = string.Empty);

        PlayPlaylistCommand = new RelayCommand<Playlist>(PlayPlaylist);
        ChangePlaylistCoverCommand = new RelayCommand<Playlist>(p => _ = ChangePlaylistCoverAsync(p));
        ClearPlaylistCoverCommand = new RelayCommand<Playlist>(ClearPlaylistCover);
        ToggleDetailCommand = new RelayCommand(ToggleDetail);

        AddTrackToPlaylistCommand = new RelayCommand<Track>(t => _ = AddTrackToPlaylistAsync(t));
        MovePlaylistToFolderCommand = new RelayCommand<Playlist>(p => _ = MovePlaylistToFolderAsync(p));

        ExitCommand = new RelayCommand(() =>
        {
            NullActionLogger.User("AppExit", "shutdown", nameof(MainViewModel));
            _ = _plugins.ShutdownAllAsync();
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.Shutdown();
        });

        OpenSettingsCommand = new RelayCommand(() =>
        {
            NullActionLogger.SettingChanged("SettingsOpened", nameof(MainViewModel));
            OpenSettings();
        });

        OpenProfileCommand = new RelayCommand(() =>
        {
            NullActionLogger.User("ProfileOpened", "profile", nameof(MainViewModel));
            OpenProfileWindow();
        });

        AboutCommand = new RelayCommand(() => Log.Information("[{Source}] About dialog requested", nameof(MainViewModel)));

        OpenDataFolderCommand = new RelayCommand(() =>
        {
            var dir = NullWavePaths.DataDir;
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
            Log.Information("[{Source}] Opened data folder: {Dir}", nameof(MainViewModel), dir);
        });

        OpenLogsCommand = new RelayCommand(() =>
        {
            var dir = NullWavePaths.LogsDir;
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
            Log.Information("[{Source}] Opened logs folder: {Dir}", nameof(MainViewModel), dir);
        });

        NavigateLibraryCommand = new RelayCommand(() => { Library.ShowAll(); CurrentPage = "Library"; LogNav("Library"); });
        NavigateRadioCommand = new RelayCommand(() => { CurrentPage = "Radio"; LogNav("Radio"); });
        NavigateAudiobooksCommand = new RelayCommand(() => { CurrentPage = "Audiobooks"; LogNav("Audiobooks"); });
        
        NavigatePlaylistsCommand = new RelayCommand(() =>
        {
            CurrentPage = "Playlists";
            Playlist.SelectFirst();
            Nav.SetPlaylistActive(Playlist.SelectedPlaylist?.Id);
            LogNav("Playlists");
        });

        NavigateToPlaylistCommand = new RelayCommand<Playlist>(p =>
        {
            if (p == null) return;
            CurrentPage = "Playlists";
            Playlist.SelectById(p.Id);
            Nav.SetPlaylistActive(p.Id);
            LogNav("Playlists");
        });

        ToggleQueueCommand = new RelayCommand(() => { Queue.IsOpen = !Queue.IsOpen; LogNav("Queue"); });
    }

    private void OpenSettings()
    {
        var win = new Views.SettingsWindow { DataContext = Settings };
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
            win.ShowDialog(desktop.MainWindow);
        else
            win.Show();
    }

    private void OpenProfileWindow()
    {
        var win = new Views.ProfileWindow { DataContext = Profile };
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
            win.ShowDialog(desktop.MainWindow);
        else
            win.Show();
    }

    private void PlayPlaylist(Playlist? playlist)
    {
        if (playlist == null || playlist.Tracks.Count == 0) return;
        Player.PlayPlaylist(playlist);
    }

    private async Task ChangePlaylistCoverAsync(Playlist? playlist)
    {
        if (playlist == null) return;
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop || desktop.MainWindow == null) return;

        var files = await desktop.MainWindow.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Choose playlist cover",
            AllowMultiple = false,
            FileTypeFilter = new[] { new Avalonia.Platform.Storage.FilePickerFileType("Images") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.webp" } } }
        });

        if (files.Count == 0) return;

        playlist.CustomArtPath = files[0].Path.LocalPath;
        _playlists.UpdatePlaylist(playlist);
        Playlist.Refresh();
        Nav.RefreshPlaylistLists();
        ToastService.Instance.Show($"Cover updated for '{playlist.Name}'.", ToastType.Success, scope: "playlist");
    }

    private void ClearPlaylistCover(Playlist? playlist)
    {
        if (playlist == null) return;
        playlist.CustomArtPath = null;
        _playlists.UpdatePlaylist(playlist);
        Playlist.Refresh();
        Nav.RefreshPlaylistLists();
    }

    private void ToggleDetail()
    {
        if (Detail.IsOpen) Detail.IsOpen = false;
        else if (Player.CurrentTrack != null) Detail.OpenFor(Player.CurrentTrack);
    }

    private async Task AddTracksToPlaylistAsync(List<Track> tracks)
    {
        if (tracks.Count == 0) return;
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop || desktop.MainWindow == null) return;

        var playlist = await new Views.AddToPlaylistDialog(_playlists).ShowDialog<Playlist?>(desktop.MainWindow);
        if (playlist == null) return;

        int added = 0;
        foreach (var t in tracks)
            if (_playlists.AddTrack(playlist.Id, t)) added++;

        ToastService.Instance.Show(
            added > 0 ? $"Added {added} track(s) to '{playlist.Name}'." : $"All tracks already in '{playlist.Name}'.",
            added > 0 ? ToastType.Success : ToastType.Info, scope: "playlist");
        Playlist.Refresh();
        Nav.RefreshPlaylistLists();
    }

    private async Task AddTrackToPlaylistAsync(Track? track)
    {
        if (track == null) return;
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop || desktop.MainWindow == null) return;

        var playlist = await new Views.AddToPlaylistDialog(_playlists).ShowDialog<Playlist?>(desktop.MainWindow);
        if (playlist == null) return;

        if (_playlists.AddTrack(playlist.Id, track))
        {
            ToastService.Instance.Show($"Added '{track.Title}' to '{playlist.Name}'.", ToastType.Success, scope: "playlist");
            Playlist.Refresh();
            Nav.RefreshPlaylistLists();
        }
        else
        {
            ToastService.Instance.Show($"'{track.Title}' is already in '{playlist.Name}'.", ToastType.Info, scope: "playlist");
        }
    }

    private async Task MovePlaylistToFolderAsync(Playlist? playlist)
    {
        if (playlist == null) return;
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop || desktop.MainWindow == null) return;

        var choice = await new Views.MoveToFolderDialog(_playlists, playlist.FolderId).ShowDialog<Views.FolderOption?>(desktop.MainWindow);
        if (choice == null) return;

        Nav.MovePlaylistToFolder(playlist.Id, choice.FolderId);
        ToastService.Instance.Show(choice.FolderId == null
            ? $"'{playlist.Name}' moved to top level."
            : $"'{playlist.Name}' moved to '{choice.Name}'.", ToastType.Success, scope: "playlist");
    }

    private static void LogNav(string destination) => NullActionLogger.User("Navigate", destination, nameof(MainViewModel));
}