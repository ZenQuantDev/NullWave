using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using NullWave.Helpers;
using NullWave.Helpers.Logging;
using NullWave.Services;
using NullWave.ViewModels.Base;
using Serilog;

namespace NullWave.ViewModels;

public class MainViewModel : ViewModelBase
{
    // ─── Services ─────────────────────────────────────────────────────────────
    private readonly KeyStoreService _keyStore = new();
    private readonly SecureDeleteService _secureDelete;
    private readonly ConfigService _config;
    private readonly LibraryService _library;
    private readonly PlaylistService _playlists = new();
    private readonly LastFmService _lastFm;
    private readonly MetadataService _metadata;
    private readonly UrlParserService _urlParser = new();
    private readonly ExportService _export = new();
    private readonly PlaybackService _playbackService = new();
    private readonly DownloadService _downloadService = new();

    // ─── Menu bar visibility (Alt-key toggle) ─────────────────────────────────
    private bool _isMenuBarVisible = false;
    public bool IsMenuBarVisible
    {
        get => _isMenuBarVisible;
        set { _isMenuBarVisible = value; OnPropertyChanged(); }
    }
    public void ToggleMenuBar() => IsMenuBarVisible = !IsMenuBarVisible;

    // ─── Child ViewModels ─────────────────────────────────────────────────────
    public TrackInputViewModel  Input    { get; }
    public LibraryViewModel     Library  { get; }
    public PlaylistViewModel    Playlist { get; }
    public ExportViewModel      Export   { get; }
    public SettingsViewModel    Settings { get; }
    public TrackDetailViewModel Detail   { get; }
    public ImportViewModel      Import   { get; }
    public PlayerViewModel      Player   { get; }
    public UserProfileViewModel Profile  { get; }

    // ─── Menu commands ────────────────────────────────────────────────────────
    public ICommand ExitCommand          { get; }
    public ICommand OpenSettingsCommand  { get; }
    public ICommand AboutCommand         { get; }
    public ICommand OpenDataFolderCommand{ get; }
    public ICommand OpenLogsCommand      { get; }

    // ─── Navigation commands (stub — will activate panels in Phase 7) ─────────
    public ICommand NavigateLibraryCommand   { get; }
    public ICommand NavigatePlaylistsCommand { get; }
    public ICommand NavigateQueueCommand     { get; }
    public ICommand NavigateStatsCommand     { get; }

    public MainViewModel()
    {
        _secureDelete = new SecureDeleteService(_keyStore);
        _config       = new ConfigService(_keyStore);
        _lastFm       = new LastFmService(_config);
        _metadata     = new MetadataService(_config, _lastFm);
        _library      = new LibraryService(_metadata);

        // ── Construct child ViewModels ────────────────────────────────────────
        Input    = new TrackInputViewModel(_library, _metadata, _urlParser);
        Library  = new LibraryViewModel(_library);
        Playlist = new PlaylistViewModel(_playlists);
        Export   = new ExportViewModel(_library, _export);
        Settings = new SettingsViewModel(_keyStore, _secureDelete);
        Detail   = new TrackDetailViewModel(_library);
        Import   = new ImportViewModel(_library, _metadata);
        Player   = new PlayerViewModel(_playbackService, _downloadService, _library);
        Profile  = new UserProfileViewModel();

        // ── Wire events ───────────────────────────────────────────────────────
        Input.TrackAdded              += Library.Refresh;
        Library.TrackDetailRequested  += Detail.OpenFor;
        Library.PlayTrackRequested    += Player.PlayTrack;
        Import.ImportCompleted        += Library.Refresh;

        // When play is pressed with no current track, start the selected track
        Player.PlaySelectedTrackRequested += () =>
        {
            if (Library.SelectedTrack != null)
                Player.PlayTrack(Library.SelectedTrack);
            else if (Library.Tracks.Count > 0)
                Player.PlayTrack(Library.Tracks[0]);
        };

        // ── Commands ──────────────────────────────────────────────────────────
        ExitCommand = new RelayCommand(() =>
        {
            NullActionLogger.User("AppExit", "shutdown", nameof(MainViewModel));
            if (Application.Current?.ApplicationLifetime is
                IClassicDesktopStyleApplicationLifetime desktop)
                desktop.Shutdown();
        });

        OpenSettingsCommand = new RelayCommand(() =>
        {
            NullActionLogger.SettingChanged("SettingsOpened", nameof(MainViewModel));
            OpenSettings();
        });

        AboutCommand = new RelayCommand(() =>
        {
            // TODO: open About window (Phase 7)
            Log.Information("[{Source}] About dialog requested", nameof(MainViewModel));
        });

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

        NavigateLibraryCommand   = new RelayCommand(() => LogNav("Library"));
        NavigatePlaylistsCommand = new RelayCommand(() => LogNav("Playlists"));
        NavigateQueueCommand     = new RelayCommand(() => LogNav("Queue"));
        NavigateStatsCommand     = new RelayCommand(() => LogNav("Stats"));

        // ── Run startup diagnostics async (fire-and-forget, safe) ────────────
        _ = RunStartupDiagnosticsAsync();
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private async System.Threading.Tasks.Task RunStartupDiagnosticsAsync()
    {
        try
        {
            var diag = new StartupDiagnosticsService(_keyStore, _library);
            await diag.RunAsync();
        }
        catch (Exception ex)
        {
            NullActionLogger.Error(nameof(MainViewModel), ex, "Startup diagnostics failed");
        }
    }

    private void OpenSettings()
    {
        var win = new Views.SettingsWindow { DataContext = Settings };
        if (Application.Current?.ApplicationLifetime is
            IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow != null)
            win.ShowDialog(desktop.MainWindow);
        else
            win.Show();
    }

    private static void LogNav(string destination)
        => NullActionLogger.User("Navigate", destination, nameof(MainViewModel));
}