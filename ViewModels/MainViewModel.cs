using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using NullWave.Helpers;
using NullWave.Services;
using NullWave.ViewModels.Base;

namespace NullWave.ViewModels;

public class MainViewModel : ViewModelBase
{
    // Services
    private readonly KeyStoreService _keyStore = new();
    private readonly SecureDeleteService _secureDelete;
    private readonly ConfigService _config;
    private readonly LibraryService _library = new();
    private readonly PlaylistService _playlists = new();
    private readonly MetadataService _metadata;
    private readonly UrlParserService _urlParser = new();
    private readonly ExportService _export = new();

    // Child ViewModels
    public TrackInputViewModel Input { get; }
    public LibraryViewModel Library { get; }
    public PlaylistViewModel Playlist { get; }
    public ExportViewModel Export { get; }
    public SettingsViewModel Settings { get; }

    // Menu commands
    public ICommand ExitCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public ICommand AboutCommand { get; }
    public ICommand OpenDataFolderCommand { get; }
    public ICommand OpenLogsCommand { get; }

    // Navigation commands (stub for now — will activate panels later)
    public ICommand NavigateLibraryCommand { get; }
    public ICommand NavigatePlaylistsCommand { get; }
    public ICommand NavigateQueueCommand { get; }
    public ICommand NavigateStatsCommand { get; }

    public MainViewModel()
    {
        _secureDelete = new SecureDeleteService(_keyStore);
        _config = new ConfigService(_keyStore);
        _metadata = new MetadataService(_config);

        Input = new TrackInputViewModel(_library, _metadata, _urlParser);
        Library = new LibraryViewModel(_library);
        Playlist = new PlaylistViewModel(_playlists);
        Export = new ExportViewModel(_library, _export);
        Settings = new SettingsViewModel(_keyStore, _secureDelete);

        Input.TrackAdded += Library.Refresh;

        ExitCommand = new RelayCommand(() =>
        {
            if (Application.Current?.ApplicationLifetime is
                IClassicDesktopStyleApplicationLifetime desktop)
                desktop.Shutdown();
        });

        OpenSettingsCommand = new RelayCommand(OpenSettings);

        AboutCommand = new RelayCommand(() =>
        {
            // TODO: open About dialog (Phase 4)
            Serilog.Log.Information("About NullWave v0.1.0");
        });

        OpenDataFolderCommand = new RelayCommand(() =>
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nullwave");
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
        });

        OpenLogsCommand = new RelayCommand(() =>
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nullwave", "logs");
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
        });

        // Navigation stubs — will swap content panels in Phase 4
        NavigateLibraryCommand = new RelayCommand(() =>
            Serilog.Log.Debug("Navigate: Library"));
        NavigatePlaylistsCommand = new RelayCommand(() =>
            Serilog.Log.Debug("Navigate: Playlists"));
        NavigateQueueCommand = new RelayCommand(() =>
            Serilog.Log.Debug("Navigate: Queue"));
        NavigateStatsCommand = new RelayCommand(() =>
            Serilog.Log.Debug("Navigate: Stats"));
    }

    private void OpenSettings()
    {
        var win = new Views.SettingsWindow
        {
            DataContext = Settings
        };
        win.Show();
    }
}