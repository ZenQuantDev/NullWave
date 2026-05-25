using System;
using System.Windows.Input;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using NullWave.Models;
using NullWave.Services;
using NullWave.Helpers;

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
    private string _searchQuery = string.Empty;
    private string _inputUrl = string.Empty;
    private string _inputTitle = string.Empty;
    private string _inputArtist = string.Empty;
    private TrackSource _selectedSource = TrackSource.Unknown;
    private Track? _selectedTrack;

    public ObservableCollection<Track> Tracks { get; } = new();
    public Array SourceOptions => Enum.GetValues(typeof(TrackSource));
    public ICommand AddTrackCommand { get; }
    public ICommand RemoveTrackCommand { get; }

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
            ? _library.GetAll()
            : _library.Search(SearchQuery);

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
    public MainViewModel()
    {
    AddTrackCommand = new RelayCommand(AddTrack);
    RemoveTrackCommand = new RelayCommand(RemoveTrack);
    }

}