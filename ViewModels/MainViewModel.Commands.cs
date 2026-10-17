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
            ShellHelper.OpenFolder(NullWavePaths.DataDir);
            Log.Information("[{Source}] Opened data folder: {Dir}", nameof(MainViewModel), NullWavePaths.DataDir);
        });

        OpenLogsCommand = new RelayCommand(() =>
        {
            ShellHelper.OpenFolder(NullWavePaths.LogsDir);
            Log.Information("[{Source}] Opened logs folder: {Dir}", nameof(MainViewModel), NullWavePaths.LogsDir);
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
        DialogService.Instance.ShowWindow(win);
    }

    private void OpenProfileWindow()
    {
        var win = new Views.ProfileWindow { DataContext = Profile };
        DialogService.Instance.ShowWindow(win);
    }

    private void PlayPlaylist(Playlist? playlist)
    {
        if (playlist == null || playlist.Tracks.Count == 0 || Player == null) return;
        Player.PlayPlaylist(playlist);
    }

    private async Task ChangePlaylistCoverAsync(Playlist? playlist)
    {
        if (playlist == null) return;
        var path = await DialogService.Instance.PickFileAsync("Choose playlist cover", "*.png", "*.jpg", "*.jpeg", "*.webp");
        if (string.IsNullOrEmpty(path)) return;
        // 2.4 FIX: Unique name with timestamp to prevent cache collisions
        var ext = System.IO.Path.GetExtension(path);
        if (string.IsNullOrEmpty(ext)) ext = ".jpg";
        var dest = System.IO.Path.Combine(NullWavePaths.ArtCacheDir, $"pl_{playlist.Id:N}_{DateTime.UtcNow.Ticks}{ext}");
        try
        {
            await using var inStream = System.IO.File.OpenRead(path);
            await using var outStream = System.IO.File.Create(dest);
            await inStream.CopyToAsync(outStream);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Playlist] cover copy failed for {Name}", playlist.Name);
            ToastService.Instance.Show($"Couldn't copy cover image: {ex.Message}", ToastType.Error, scope: "playlist");
            return;
        }
        // 2.4 FIX: Delete old cover file and invalidate cache
        var oldPath = playlist.CustomArtPath;
        if (!string.IsNullOrEmpty(oldPath) && System.IO.File.Exists(oldPath))
        {
            try { System.IO.File.Delete(oldPath); } catch { }
            BitmapCacheService.InvalidatePath(oldPath);
        }

        playlist.CustomArtPath = dest;
        _playlists.UpdatePlaylist(playlist);
        Playlist.Refresh();
        Nav.RefreshPlaylistLists();
        ToastService.Instance.Show($"Cover updated for '{playlist.Name}'.", ToastType.Success, scope: "playlist");
    }

    private void ClearPlaylistCover(Playlist? playlist)
    {
        if (playlist == null) return;
        
        // 2.4 FIX: Delete the file and invalidate cache
        var oldPath = playlist.CustomArtPath;
        if (!string.IsNullOrEmpty(oldPath) && File.Exists(oldPath))
        {
            try { File.Delete(oldPath); } catch { }
            BitmapCacheService.InvalidatePath(oldPath);
        }

        playlist.CustomArtPath = null;
        _playlists.UpdatePlaylist(playlist);
        Playlist.Refresh();
        Nav.RefreshPlaylistLists();
    }

    private void ToggleDetail()
    {
        if (Player == null) return;
        if (Detail.IsOpen) Detail.IsOpen = false;
        else if (Player.CurrentTrack != null) Detail.OpenFor(Player.CurrentTrack);
    }

    private async Task AddTracksToPlaylistAsync(List<Track> tracks)
    {
        if (tracks.Count == 0) return;
        var playlist = await DialogService.Instance.ShowDialogAsync<Playlist?>(new Views.AddToPlaylistDialog(_playlists));
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
        var playlist = await DialogService.Instance.ShowDialogAsync<Playlist?>(new Views.AddToPlaylistDialog(_playlists));
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
        var choice = await DialogService.Instance.ShowDialogAsync<Views.FolderOption?>(new Views.MoveToFolderDialog(_playlists, playlist.FolderId));
        if (choice == null) return;
        Nav.MovePlaylistToFolder(playlist.Id, choice.FolderId);
        ToastService.Instance.Show(choice.FolderId == null
            ? $"'{playlist.Name}' moved to top level."
            : $"'{playlist.Name}' moved to '{choice.Name}'.", ToastType.Success, scope: "playlist");
    }

    private static void LogNav(string destination) => NullActionLogger.User("Navigate", destination, nameof(MainViewModel));
}