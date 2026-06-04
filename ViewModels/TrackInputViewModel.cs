using System;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using NullWave.Helpers;
using NullWave.Helpers.Logging;
using NullWave.Models;
using NullWave.Services;
using NullWave.ViewModels.Base;
using Serilog;

namespace NullWave.ViewModels;

public class TrackInputViewModel : ViewModelBase
{
    private readonly LibraryService    _library;
    private readonly MetadataService   _metadata;
    private readonly UrlParserService  _urlParser;

    private string      _inputUrl           = string.Empty;
    private string      _lastFetchedUrl     = string.Empty;
    private string      _inputTitle         = string.Empty;
    private string      _inputArtist        = string.Empty;
    private TrackSource _selectedSource     = TrackSource.Unknown;
    private bool        _isFetching;

    public Array    SourceOptions      => Enum.GetValues(typeof(TrackSource));
    public ICommand AddTrackCommand    { get; }
    public ICommand AddLocalFileCommand{ get; }

    public event Action? TrackAdded;

    public bool IsFetching
    {
        get => _isFetching;
        set { _isFetching = value; OnPropertyChanged(); }
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

    public TrackInputViewModel(
        LibraryService   library,
        MetadataService  metadata,
        UrlParserService urlParser)
    {
        _library   = library;
        _metadata  = metadata;
        _urlParser = urlParser;

        AddTrackCommand     = new RelayCommand(AddTrack);
        AddLocalFileCommand = new RelayCommand(async () => await AddLocalFileAsync());
    }

    public void AddTrack()
    {
        // Require at least a title OR a URL — don't silently do nothing
        var title = InputTitle.Trim();
        var url   = InputUrl.Trim();

        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(url))
        {
            Log.Warning("[{Source}] AddTrack called with no title and no URL — ignored",
                nameof(TrackInputViewModel));
            return;
        }

        // If no title, use URL as fallback title (metadata fetch may still fill it in)
        if (string.IsNullOrWhiteSpace(title))
            title = url;

        var track = new Track
        {
            Title  = title,
            Artist = InputArtist.Trim(),
            Url    = string.IsNullOrWhiteSpace(url) ? null : url,
            Source = SelectedSource
        };

        _library.Add(track);
        NullActionLogger.TrackAdded(track.Id.ToString(), url, nameof(TrackInputViewModel));
        ClearInputs();
        TrackAdded?.Invoke();
    }

    private async Task FetchMetadataAsync(string url)
    {
        IsFetching = true;
        try
        {
            var (title, artist) = await _metadata.FetchFromUrlAsync(url);
            if (string.IsNullOrWhiteSpace(InputTitle))  InputTitle  = title;
            if (string.IsNullOrWhiteSpace(InputArtist)) InputArtist = artist;
            Log.Information("[{Source}] Metadata fetched: {Title} by {Artist}",
                nameof(TrackInputViewModel), title, artist);
        }
        catch (Exception ex)
        {
            NullActionLogger.Error(nameof(TrackInputViewModel), ex,
                $"Metadata fetch failed for {url}");
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

        var files = await window.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title         = "Select Audio File",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Audio Files")
                    {
                        Patterns = new[] { "*.mp3", "*.flac", "*.wav",
                                           "*.ogg", "*.m4a", "*.aac" }
                    }
                }
            });

        if (files.Count == 0) return;

        var filePath = files[0].Path.LocalPath;
        if (!_urlParser.IsSupportedAudioFile(filePath)) return;

        var (title, artist) = _metadata.FetchFromLocalFile(filePath);
        var track = new Track
        {
            Title    = title,
            Artist   = artist,
            FilePath = filePath,
            Source   = TrackSource.Local
        };

        _library.Add(track);
        NullActionLogger.TrackAdded(track.Id.ToString(), filePath, nameof(TrackInputViewModel));
        TrackAdded?.Invoke();
    }

    private void ClearInputs()
    {
        InputUrl       = string.Empty;
        InputTitle     = string.Empty;
        InputArtist    = string.Empty;
        SelectedSource = TrackSource.Unknown;
    }
}