using System;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using NullWave.Models;
using NullWave.Services;
using NullWave.Helpers;
using Serilog;

namespace NullWave.ViewModels;

public class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class MainViewModel : ViewModelBase
{
    private readonly LibraryService _library = new();
    private readonly MetadataService _metadata = new();
    private readonly UrlParserService _urlParser = new();
    private readonly ExportService _export = new();
    private readonly PlaylistService _playlists = new();
    private string _searchQuery = string.Empty;
    private string _inputUrl = string.Empty;
    private string _lastFetchedUrl = string.Empty;
    private string _inputTitle = string.Empty;
    private string _inputArtist = string.Empty;
    private TrackSource _selectedSource = TrackSource.Unknown;
    private Track? _selectedTrack;
    private Playlist? _selectedPlaylist;
    private bool _isFetching;
    private SortField _currentSort = SortField.DateAdded;
    private bool _sortAscending = true;

    public ObservableCollection<Track> Tracks { get; } = new();
    public ObservableCollection<Playlist> Playlists { get; } = new();
    public Array SourceOptions => Enum.GetValues(typeof(TrackSource));
    public Array SortOptions => Enum.GetValues(typeof(SortField));
    public ICommand AddTrackCommand { get; }
    public ICommand RemoveTrackCommand { get; }
    public ICommand AddLocalFileCommand { get; }
    public ICommand ExportJsonCommand { get; }
    public ICommand ExportCsvCommand { get; }
    public ICommand ToggleFavoriteCommand { get; }
    public ICommand AddToQueueCommand { get; }
    public ICommand RecordPlayCommand { get; }
    public ICommand CreatePlaylistCommand { get; }
    public ICommand RemovePlaylistCommand { get; }
    public ICommand AddToPlaylistCommand { get; }
    public ICommand RemoveFromPlaylistCommand { get; }

    public bool IsFetching
    {
        get => _isFetching;
        set { _isFetching = value; OnPropertyChanged(); }
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set { _searchQuery = value; OnPropertyChanged(); RefreshTracks(); }
    }

    public string InputUrl
    {
        get => _inputUrl;
        set
        {
            _inputUrl = value;
            OnPropertyChanged();
            SelectedSource = SourceDetector.Detect(value);
            if (_urlParser.IsValidUrl(value) && value != _lastFetchedUrl)
            {
                _lastFetchedUrl = value;
                _ = FetchMetadataAsync(value);
            }
        }
    }

    public string InputTitle
    {
        get => _inputTitle;
        set { _inputTitle = value; OnPropertyChanged(); }
    }

    public string InputArtist
    {
        get => _inputArtist;
        set { _inputArtist = value; OnPropertyChanged(); }
    }

    public TrackSource SelectedSource
    {
        get => _selectedSource;
        set { _selectedSource = value; OnPropertyChanged(); }
    }

    public Track? SelectedTrack
    {
        get => _selectedTrack;
        set { _selectedTrack = value; OnPropertyChanged(); }
    }

    public SortField CurrentSort
    {
        get => _currentSort;
        set { _currentSort = value; OnPropertyChanged(); RefreshTracks(); }
    }

    public bool SortAscending
    {
        get => _sortAscending;
        set { _sortAscending = value; OnPropertyChanged(); RefreshTracks(); }
    }

    public Playlist? SelectedPlaylist
    {
        get => _selectedPlaylist;
        set { _selectedPlaylist = value; OnPropertyChanged(); }
    }

    public void AddTrack()
    {
        if (string.IsNullOrWhiteSpace(InputTitle)) return;

        var track = new Track
        {
            Title = InputTitle,
            Artist = InputArtist,
            Url = InputUrl,
            Source = SelectedSource
        };

        _library.Add(track);
        RefreshTracks();
        ClearInputs();
    }

    public void RemoveTrack()
    {
        if (SelectedTrack == null) return;
        _library.Remove(SelectedTrack.Id);
        SelectedTrack = null;
        RefreshTracks();
    }

    private void RefreshTracks()
    {
        Tracks.Clear();
        var results = string.IsNullOrWhiteSpace(SearchQuery)
            ? _library.GetSorted(CurrentSort, SortAscending)
            : _library.Search(SearchQuery, CurrentSort, SortAscending);

        foreach (var track in results)
            Tracks.Add(track);
    }

    private void ClearInputs()
    {
        InputUrl = string.Empty;
        InputTitle = string.Empty;
        InputArtist = string.Empty;
        SelectedSource = TrackSource.Unknown;
    }

    private async Task FetchMetadataAsync(string url)
    {
        IsFetching = true;
        try
        {
            var (title, artist) = await _metadata.FetchFromUrlAsync(url);
            if (string.IsNullOrWhiteSpace(InputTitle))
                InputTitle = title;
            if (string.IsNullOrWhiteSpace(InputArtist))
                InputArtist = artist;
        }
        finally
        {
            IsFetching = false;
        }
    }

    private async Task AddLocalFileAsync()
    {
        var window = Application.Current?.ApplicationLifetime is
            IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow : null;

        if (window == null) return;

        var storageProvider = window.StorageProvider;
        var audioTypes = new FilePickerFileType("Audio Files")
        {
            Patterns = new[] { "*.mp3", "*.flac", "*.wav", "*.ogg", "*.m4a", "*.aac" }
        };

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Audio File",
            AllowMultiple = false,
            FileTypeFilter = new[] { audioTypes }
        });

        if (files.Count == 0) return;

        var file = files[0];
        var filePath = file.Path.LocalPath;

        if (!_urlParser.IsSupportedAudioFile(filePath)) return;

        var (title, artist) = _metadata.FetchFromLocalFile(filePath);

        var track = new Track
        {
            Title = title,
            Artist = artist,
            FilePath = filePath,
            Source = TrackSource.Local
        };

        _library.Add(track);
        RefreshTracks();
    }

    private async Task ExportAsync(string format)
    {
        var window = Application.Current?.ApplicationLifetime is
            IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow : null;

        if (window == null) return;

        var storageProvider = window.StorageProvider;
        var fileType = new FilePickerFileType(format.ToUpper())
        {
            Patterns = new[] { $"*.{format}" }
        };

        var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Library",
            DefaultExtension = format,
            FileTypeChoices = new[] { fileType }
        });

        if (file == null) return;

        var path = file.Path.LocalPath;
        if (string.IsNullOrWhiteSpace(path)) return;

        if (format == "json")
            _export.ExportToJson(_library.GetAll(), path);
        else
            _export.ExportToCsv(_library.GetAll(), path);
    }

    private void ToggleFavorite()
    {
        if (SelectedTrack == null) return;
        _library.ToggleFavorite(SelectedTrack.Id);
        RefreshTracks();
    }

    private void AddToQueue()
    {
        if (SelectedTrack == null) return;
        _library.AddToQueue(SelectedTrack.Id);
    }

    private void RecordPlay()
    {
        if (SelectedTrack == null) return;
        _library.RecordPlay(SelectedTrack.Id);
        RefreshTracks();
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

    private void AddToPlaylist()
    {
        if (SelectedTrack == null || SelectedPlaylist == null) return;
        _playlists.AddTrack(SelectedPlaylist.Id, SelectedTrack);
    }

    private void RemoveFromPlaylist()
    {
        if (SelectedTrack == null || SelectedPlaylist == null) return;
        _playlists.RemoveTrack(SelectedPlaylist.Id, SelectedTrack.Id);
    }

    public MainViewModel()
    {
        AddTrackCommand = new RelayCommand(AddTrack);
        RemoveTrackCommand = new RelayCommand(RemoveTrack);
        AddLocalFileCommand = new RelayCommand(async () => await AddLocalFileAsync());
        ExportJsonCommand = new RelayCommand(async () => await ExportAsync("json"));
        ExportCsvCommand = new RelayCommand(async () => await ExportAsync("csv"));
        ToggleFavoriteCommand = new RelayCommand(ToggleFavorite);
        AddToQueueCommand = new RelayCommand(AddToQueue);
        RecordPlayCommand = new RelayCommand(RecordPlay);
        CreatePlaylistCommand = new RelayCommand(CreatePlaylist);
        RemovePlaylistCommand = new RelayCommand(RemovePlaylist);
        AddToPlaylistCommand = new RelayCommand(AddToPlaylist);
        RemoveFromPlaylistCommand = new RelayCommand(RemoveFromPlaylist);
    }

}