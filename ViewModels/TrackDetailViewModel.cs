using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;  // ← ADD THIS for IClipboard and TopLevel
using NullWave.Helpers;
using NullWave.Models;
using NullWave.Services;
using NullWave.ViewModels.Base;
using Serilog;

namespace NullWave.ViewModels;

public class TrackDetailViewModel : ViewModelBase
{
    private readonly LibraryService _library;
    private Track? _track;
    private bool _isOpen;
    private string _editTitle = string.Empty;
    private string _editArtist = string.Empty;
    private string _editNotes = string.Empty;
    private string _newTag = string.Empty;
    private string _copyStatus = "Copy";

    public bool IsOpen
    {
        get => _isOpen;
        set { _isOpen = value; OnPropertyChanged(); OnPropertyChanged(nameof(PanelWidth)); }
    }

    public double PanelWidth => _isOpen ? 320 : 0;

    public Track? Track
    {
        get => _track;
        set
        {
            _track = value;
            OnPropertyChanged();
            if (value != null) LoadFromTrack(value);
        }
    }

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

    public string DisplayUrl => _track?.Url ?? _track?.FilePath ?? "—";
    public string DisplaySource => _track?.Source.ToString() ?? "—";
    public string DisplayDateAdded => _track?.DateAdded.ToString("MMMM dd, yyyy") ?? "—";
    public string DisplayLastPlayed => _track?.LastPlayed?.ToString("MMMM dd, yyyy HH:mm") ?? "Never";
    public string DisplayPlayCount => _track?.PlayCount.ToString() ?? "0";
    public bool IsFavorite => _track?.IsFavorite ?? false;

    public ICommand SaveCommand { get; }
    public ICommand CloseCommand { get; }
    public ICommand AddTagCommand { get; }
    public ICommand RemoveTagCommand { get; }
    public ICommand ToggleFavoriteCommand { get; }
    public ICommand CopyUrlCommand { get; }

    public TrackDetailViewModel(LibraryService library)
    {
        _library = library;
        SaveCommand = new RelayCommand(Save);
        CloseCommand = new RelayCommand(() => IsOpen = false);
        AddTagCommand = new RelayCommand(AddTag);
        RemoveTagCommand = new RelayCommand<string>(RemoveTag);
        ToggleFavoriteCommand = new RelayCommand(ToggleFavorite);
        CopyUrlCommand = new RelayCommand(async () => await CopyUrlAsync());
    }

    public void OpenFor(Track track)
    {
        Track = track;
        IsOpen = true;
    }

    private void LoadFromTrack(Track track)
    {
        EditTitle = track.Title;
        EditArtist = track.Artist;
        EditNotes = track.Notes ?? string.Empty;
        Tags.Clear();
        foreach (var tag in track.Tags) Tags.Add(tag);
        
        OnPropertyChanged(nameof(DisplayUrl));
        OnPropertyChanged(nameof(DisplaySource));
        OnPropertyChanged(nameof(DisplayDateAdded));
        OnPropertyChanged(nameof(DisplayLastPlayed));
        OnPropertyChanged(nameof(DisplayPlayCount));
        OnPropertyChanged(nameof(IsFavorite));
    }

    private void Save()
    {
        if (_track == null) return;
        _track.Title = EditTitle;
        _track.Artist = EditArtist;
        _track.Notes = EditNotes;
        _track.Tags.Clear();
        foreach (var tag in Tags) _track.Tags.Add(tag);
        
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
        if (_track == null) return;
        _library.ToggleFavorite(_track.Id);
        OnPropertyChanged(nameof(IsFavorite));
    }

    private async Task CopyUrlAsync()
    {
        var url = _track?.Url ?? _track?.FilePath;
        if (string.IsNullOrEmpty(url)) return;

        try
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                && desktop.MainWindow != null)
            {
                var topLevel = TopLevel.GetTopLevel(desktop.MainWindow);
                var clipboard = topLevel?.Clipboard;
                
                if (clipboard != null)
                {
                    // Try to find the correct method using reflection
                    var setTextMethod = clipboard.GetType().GetMethod("SetTextAsync");
                    if (setTextMethod != null)
                    {
                        var task = (Task?)setTextMethod.Invoke(clipboard, new object[] { url });
                        if (task != null) await task;
                        
                        CopyStatus = "Copied!";
                        await Task.Delay(2000);
                        CopyStatus = "Copy";
                        Log.Debug("URL copied to clipboard: {Url}", url);
                    }
                    else
                    {
                        Log.Warning("SetTextAsync method not found on IClipboard");
                        CopyStatus = "Clipboard API unavailable";
                        await Task.Delay(2000);
                        CopyStatus = "Copy";
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to copy URL to clipboard");
            CopyStatus = "Copy failed";
            await Task.Delay(2000);
            CopyStatus = "Copy";
        }
    }
}