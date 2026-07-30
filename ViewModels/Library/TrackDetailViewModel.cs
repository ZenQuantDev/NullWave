using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using NullWave.Helpers;
using NullWave.Helpers.Logging;
using NullWave.Models;
using NullWave.Services;
using NullWave.Services.Integration;
using NullWave.Services.Plugins;
using NullWave.ViewModels.Base;
using Serilog;

namespace NullWave.ViewModels;

public class TrackDetailViewModel : ViewModelBase
{
    private readonly LibraryService _library;
    private readonly PluginManager _plugins;
    private Track? _currentTrack;
    private bool _isOpen;
    private string _editTitle = string.Empty;
    private string _editArtist = string.Empty;
    private string _editNotes = string.Empty;
    private string _newTag = string.Empty;
    private string _copyStatus = "Copy";
    private bool _isCopying;

    private string? _artistBio;
    public string? ArtistBio
    {
        get => _artistBio;
        set { _artistBio = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasArtistBio)); }
    }

    private string? _artistListeners;
    public string? ArtistListeners
    {
        get => _artistListeners;
        set { _artistListeners = value; OnPropertyChanged(); }
    }

    private bool _isLoadingArtistInfo;
    public bool IsLoadingArtistInfo
    {
        get => _isLoadingArtistInfo;
        set { _isLoadingArtistInfo = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowLibraryFallback)); }
    }

    private int _libraryArtistTrackCount;
    public int LibraryArtistTrackCount
    {
        get => _libraryArtistTrackCount;
        set { _libraryArtistTrackCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowLibraryFallback)); }
    }

    public bool HasArtistBio => !string.IsNullOrWhiteSpace(ArtistBio);
    public bool ShowLibraryFallback => !IsLoadingArtistInfo && !HasArtistBio && LibraryArtistTrackCount > 0;

    public bool IsOpen
    {
        get => _isOpen;
        set
        {
            _isOpen = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PanelWidth));
            OnPropertyChanged(nameof(PanelOpacity));

            if (!_isOpen && _currentTrack != null)
            {
                _currentTrack.PropertyChanged -= OnTrackPropertyChanged;
                _currentTrack = null;
            }
        }
    }

    public double PanelWidth => _isOpen ? 320 : 0;
    public double PanelOpacity => _isOpen ? 1.0 : 0.0;

    public string EditTitle
    {
        get => _editTitle;
        set { _editTitle = value; OnPropertyChanged(); }
    }

    public string EditArtist
    {
        get => _editArtist;
        set { _editArtist = value; OnPropertyChanged(); }
    }

    public string EditNotes
    {
        get => _editNotes;
        set { _editNotes = value; OnPropertyChanged(); }
    }

    public string NewTag
    {
        get => _newTag;
        set { _newTag = value; OnPropertyChanged(); }
    }

    public string CopyStatus
    {
        get => _copyStatus;
        set { _copyStatus = value; OnPropertyChanged(); }
    }

    public ObservableCollection<string> Tags { get; } = new();

    public string? CurrentTrackArtPath => _currentTrack?.AlbumArtPath;
    public string DisplayUrl => _currentTrack?.Url ?? _currentTrack?.FilePath ?? "-";
    public string DisplaySource => _currentTrack?.Source.ToString() ?? "-";
    public string DisplayDateAdded => _currentTrack?.DateAdded.ToString("MMMM dd, yyyy") ?? "-";
    public string DisplayLastPlayed => _currentTrack?.LastPlayed?.ToString("MMMM dd, yyyy HH:mm") ?? "Never";
    public string DisplayPlayCount => _currentTrack?.PlayCount.ToString() ?? "0";
    public bool IsFavorite => _currentTrack?.IsFavorite ?? false;

    public ICommand SaveCommand { get; }
    public ICommand CloseCommand { get; }
    public ICommand AddTagCommand { get; }
    public ICommand RemoveTagCommand { get; }
    public ICommand ToggleFavoriteCommand { get; }
    public ICommand CopyUrlCommand { get; }
    public ICommand RelinkFileCommand { get; }

    public TrackDetailViewModel(LibraryService library, PluginManager plugins)
    {
        _library = library;
        _plugins = plugins;
        SaveCommand = new RelayCommand(Save);
        CloseCommand = new RelayCommand(() => IsOpen = false);
        AddTagCommand = new RelayCommand(AddTag);
        RemoveTagCommand = new RelayCommand<string>(RemoveTag);
        ToggleFavoriteCommand = new RelayCommand(ToggleFavorite);
        CopyUrlCommand = new RelayCommand(async () => await CopyUrlAsync());
        RelinkFileCommand = new RelayCommand(async () => await RelinkFileAsync());
    }

    public void OpenFor(Track track)
    {
        if (_currentTrack != null)
            _currentTrack.PropertyChanged -= OnTrackPropertyChanged;

        _currentTrack = track;
        track.PropertyChanged += OnTrackPropertyChanged;

        EditTitle = track.Title;
        EditArtist = track.Artist;
        EditNotes = track.Notes ?? string.Empty;

        Tags.Clear();
        foreach (var tag in track.Tags) Tags.Add(tag);

        RefreshDisplayProperties();
        IsOpen = true;

        ArtistBio = null;
        ArtistListeners = null;
        LibraryArtistTrackCount = 0;
        _ = LoadArtistInfoAsync(track);
    }

    private void OnTrackPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        RefreshDisplayProperties();
    }

    private void RefreshDisplayProperties()
    {
        OnPropertyChanged(nameof(CurrentTrackArtPath));
        OnPropertyChanged(nameof(DisplayUrl));
        OnPropertyChanged(nameof(DisplaySource));
        OnPropertyChanged(nameof(DisplayDateAdded));
        OnPropertyChanged(nameof(DisplayLastPlayed));
        OnPropertyChanged(nameof(DisplayPlayCount));
        OnPropertyChanged(nameof(IsFavorite));
    }

    private void Save()
    {
        if (_currentTrack == null) return;

        _currentTrack.Title = EditTitle.Trim();
        _currentTrack.Artist = EditArtist.Trim();
        _currentTrack.Notes = EditNotes;
        _currentTrack.TitleForceCleaned = true;

        _currentTrack.Tags.Clear();
        foreach (var tag in Tags) _currentTrack.Tags.Add(tag);

        _library.Update(_currentTrack);

        string alteredFieldsSummary = $"Title=\"{EditTitle}\", Artist=\"{EditArtist}\", TotalTagsCount={Tags.Count}";
        NullActionLogger.TrackEdited(_currentTrack.Id.ToString(), alteredFieldsSummary, "TrackDetailViewModel");
        Log.Information("Track details saved: {Title}", EditTitle);
        ToastService.Instance.Show("Track details saved.", ToastType.Success);
    }

    private void AddTag()
    {
        var tag = NewTag.Trim();
        if (string.IsNullOrWhiteSpace(tag) || Tags.Contains(tag)) return;

        Tags.Add(tag);
        NewTag = string.Empty;
    }

    private void RemoveTag(string? tag)
    {
        if (tag == null) return;
        Tags.Remove(tag);
    }

    private void ToggleFavorite()
    {
        if (_currentTrack == null) return;

        bool expectedNewState = !_currentTrack.IsFavorite;
        _library.ToggleFavorite(_currentTrack.Id);

        NullActionLogger.FavoriteToggled(_currentTrack.Id.ToString(), expectedNewState, "TrackDetailViewModel");
        OnPropertyChanged(nameof(IsFavorite));
    }

    private async Task CopyUrlAsync()
    {
        if (_isCopying) return;

        var url = _currentTrack?.Url ?? _currentTrack?.FilePath;
        if (string.IsNullOrEmpty(url)) return;

        try
        {
            _isCopying = true;
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
            {
                var clipboard = desktop.MainWindow.Clipboard;
                if (clipboard != null)
                {
                    await clipboard.SetTextAsync(url);
                    CopyStatus = "Copied!";
                    await Task.Delay(2000);
                    Log.Debug("URL copied to clipboard: {Url}", url);
                    return;
                }
            }
            CopyStatus = "Failed";
            await Task.Delay(2000);
        }
        catch (Exception ex)
        {
            NullActionLogger.Error("TrackDetailViewModel", ex, "Failed to copy target details locator route asset down to platform window clipboard space layout context.");
            CopyStatus = "Failed";
            await Task.Delay(2000);
        }
        finally
        {
            CopyStatus = "Copy";
            _isCopying = false;
        }
    }

    private async Task RelinkFileAsync()
    {
        if (_currentTrack == null) return;

        var window = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow : null;
        if (window == null) return;

        var files = await window.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Select the correct audio file",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new Avalonia.Platform.Storage.FilePickerFileType("Audio Files")
                { Patterns = new[] { "*.mp3", "*.flac", "*.wav", "*.ogg", "*.m4a", "*.aac" } }
            }
        });

        if (files.Count == 0) return;

        var newPath = files[0].Path.LocalPath;
        _currentTrack.FilePath = newPath;
        _library.RefreshAlbumArt(_currentTrack);

        NullActionLogger.TrackEdited(_currentTrack.Id.ToString(), $"FilePath relinked to \"{newPath}\"", "TrackDetailViewModel");
        Log.Information("Track relinked: {Title} → {Path}", _currentTrack.Title, newPath);
        ToastService.Instance.Show($"'{_currentTrack.Title}' relinked to new file.", ToastType.Success);

        RefreshDisplayProperties();
    }

    private async Task LoadArtistInfoAsync(Track track)
    {
        var trackId = track.Id;
        IsLoadingArtistInfo = true;

        var primaryArtist = LibraryService.SplitArtistCredits(track.Artist).FirstOrDefault() ?? track.Artist;

        LastFmArtistInfo? info = null;
        if (_plugins.Get<LastFmMetadataProvider>() is { } provider)
        {
            try { info = await provider.GetArtistInfoAsync(primaryArtist); }
            catch (Exception ex) { Log.Warning(ex, "[TrackDetailViewModel] Artist info fetch failed for {Artist}", primaryArtist); }
        }

        if (_currentTrack == null || _currentTrack.Id != trackId) return;

        if (info != null && !string.IsNullOrWhiteSpace(info.Bio))
        {
            ArtistBio = info.Bio;
            ArtistListeners = info.Listeners;
        }
        else
        {
            var normalizedTarget = LibraryService.NormalizeArtistKey(primaryArtist);
            LibraryArtistTrackCount = _library.GetAll()
                .Count(t => LibraryService.SplitArtistCredits(t.Artist)
                    .Any(name => LibraryService.NormalizeArtistKey(name) == normalizedTarget));
        }

        IsLoadingArtistInfo = false;
    }
}