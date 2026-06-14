using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using NullWave.Helpers;
using NullWave.Models;
using NullWave.Services;
using NullWave.ViewModels.Base;
using Serilog;

namespace NullWave.ViewModels;

public class TrackDetailViewModel : ViewModelBase
{
    private readonly LibraryService _library;
    private Track? _currentTrack;
    private bool _isOpen;
    private string _editTitle  = string.Empty;
    private string _editArtist = string.Empty;
    private string _editNotes  = string.Empty;
    private string _newTag     = string.Empty;
    private string _copyStatus = "Copy";

    public bool IsOpen
    {
        get => _isOpen;
        set { _isOpen = value; OnPropertyChanged(); OnPropertyChanged(nameof(PanelWidth)); }
    }

    public double PanelWidth => _isOpen ? 320 : 0;

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
    public string DisplayUrl        => _currentTrack?.Url ?? _currentTrack?.FilePath ?? "—";
    public string DisplaySource     => _currentTrack?.Source.ToString() ?? "—";
    public string DisplayDateAdded  => _currentTrack?.DateAdded.ToString("MMMM dd, yyyy") ?? "—";
    public string DisplayLastPlayed => _currentTrack?.LastPlayed?.ToString("MMMM dd, yyyy HH:mm") ?? "Never";
    public string DisplayPlayCount  => _currentTrack?.PlayCount.ToString() ?? "0";
    public bool   IsFavorite        => _currentTrack?.IsFavorite ?? false;

    public ICommand SaveCommand            { get; }
    public ICommand CloseCommand           { get; }
    public ICommand AddTagCommand          { get; }
    public ICommand RemoveTagCommand       { get; }
    public ICommand ToggleFavoriteCommand  { get; }
    public ICommand CopyUrlCommand         { get; }

    public TrackDetailViewModel(LibraryService library)
    {
        _library = library;
        SaveCommand           = new RelayCommand(Save);
        CloseCommand          = new RelayCommand(() => IsOpen = false);
        AddTagCommand         = new RelayCommand(AddTag);
        RemoveTagCommand      = new RelayCommand<string>(RemoveTag);
        ToggleFavoriteCommand = new RelayCommand(ToggleFavorite);
        CopyUrlCommand        = new RelayCommand(async () => await CopyUrlAsync());
    }

    public void OpenFor(Track track)
    {
        // Unsubscribe from previous track
        if (_currentTrack != null)
            _currentTrack.PropertyChanged -= OnTrackPropertyChanged;

        _currentTrack = track;
        track.PropertyChanged += OnTrackPropertyChanged;

        EditTitle  = track.Title;
        EditArtist = track.Artist;
        EditNotes  = track.Notes ?? string.Empty;

        Tags.Clear();
        foreach (var tag in track.Tags) Tags.Add(tag);

        RefreshDisplayProperties();
        IsOpen = true;
    }

    private void OnTrackPropertyChanged(
        object? sender, System.ComponentModel.PropertyChangedEventArgs e)
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
        _currentTrack.Title  = EditTitle;
        _currentTrack.Artist = EditArtist;
        _currentTrack.Notes  = EditNotes;
        _currentTrack.Tags.Clear();
        foreach (var tag in Tags) _currentTrack.Tags.Add(tag);
        _library.Update(_currentTrack);
        Log.Information("Track details saved: {Title}", EditTitle);
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
        if (tag != null) Tags.Remove(tag);
    }

    private void ToggleFavorite()
    {
        if (_currentTrack == null) return;
        _library.ToggleFavorite(_currentTrack.Id);
        OnPropertyChanged(nameof(IsFavorite));
    }

    private async Task CopyUrlAsync()
    {
        var url = _currentTrack?.Url ?? _currentTrack?.FilePath;
        if (string.IsNullOrEmpty(url)) return;

        try
        {
            if (Application.Current?.ApplicationLifetime
                    is IClassicDesktopStyleApplicationLifetime desktop
                && desktop.MainWindow != null)
            {
                var clipboard = TopLevel.GetTopLevel(desktop.MainWindow)?.Clipboard;
                if (clipboard != null)
                {
                    await clipboard.SetTextAsync(url);
                    CopyStatus = "Copied!";
                    await Task.Delay(2000);
                    CopyStatus = "Copy";
                    Log.Debug("URL copied to clipboard: {Url}", url);
                    return;
                }
            }
            CopyStatus = "Failed";
            await Task.Delay(2000);
            CopyStatus = "Copy";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to copy URL to clipboard");
            CopyStatus = "Failed";
            await Task.Delay(2000);
            CopyStatus = "Copy";
        }
    }
}