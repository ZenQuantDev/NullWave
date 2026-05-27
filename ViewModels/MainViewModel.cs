using NullWave.Services;
using NullWave.ViewModels.Base;

namespace NullWave.ViewModels;

public class MainViewModel : ViewModelBase
{
    // Services (shared across child ViewModels)
    private readonly LibraryService _library = new();
    private readonly PlaylistService _playlists = new();
    private readonly ConfigService _config = new();
    private readonly MetadataService _metadata;
    private readonly UrlParserService _urlParser = new();
    private readonly ExportService _export = new();

    // Child ViewModels
    public TrackInputViewModel Input { get; }
    public LibraryViewModel Library { get; }
    public PlaylistViewModel Playlist { get; }
    public ExportViewModel Export { get; }

    public MainViewModel()
    {
        _metadata = new MetadataService(_config);
        Input = new TrackInputViewModel(_library, _metadata, _urlParser);
        Library = new LibraryViewModel(_library);
        Playlist = new PlaylistViewModel(_playlists);
        Export = new ExportViewModel(_library, _export);

        // Wire up events between child ViewModels
        Input.TrackAdded += Library.Refresh;
    }
}