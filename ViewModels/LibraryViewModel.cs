using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using NullWave.Helpers;
using NullWave.Models;
using NullWave.Services;
using NullWave.ViewModels.Base;

namespace NullWave.ViewModels;

public class LibraryViewModel : ViewModelBase
{
    private readonly LibraryService _library;
    private string _searchQuery = string.Empty;
    private Track? _selectedTrack;
    private SortField _currentSort = SortField.DateAdded;
    private bool _sortAscending = true;

    public ObservableCollection<Track> Tracks { get; } = new();
    public Array SortOptions => Enum.GetValues(typeof(SortField));
    public ICommand RemoveTrackCommand { get; }
    public ICommand ToggleFavoriteCommand { get; }
    public ICommand AddToQueueCommand { get; }
    public ICommand RecordPlayCommand { get; }

    public string SearchQuery
    {
        get => _searchQuery;
        set { _searchQuery = value; OnPropertyChanged(); Refresh(); }
    }

    public Track? SelectedTrack
    {
        get => _selectedTrack;
        set { _selectedTrack = value; OnPropertyChanged(); }
    }

    public SortField CurrentSort
    {
        get => _currentSort;
        set { _currentSort = value; OnPropertyChanged(); Refresh(); }
    }

    public bool SortAscending
    {
        get => _sortAscending;
        set { _sortAscending = value; OnPropertyChanged(); Refresh(); }
    }

    public LibraryViewModel(LibraryService library)
    {
        _library = library;
        RemoveTrackCommand = new RelayCommand(RemoveTrack);
        ToggleFavoriteCommand = new RelayCommand(ToggleFavorite);
        AddToQueueCommand = new RelayCommand(AddToQueue);
        RecordPlayCommand = new RelayCommand(RecordPlay);
    }

    public void Refresh()
    {
        Tracks.Clear();
        var results = string.IsNullOrWhiteSpace(SearchQuery)
            ? _library.GetSorted(CurrentSort, SortAscending)
            : _library.Search(SearchQuery, CurrentSort, SortAscending);

        foreach (var track in results)
            Tracks.Add(track);
    }

    private void RemoveTrack()
    {
        if (SelectedTrack == null) return;
        _library.Remove(SelectedTrack.Id);
        SelectedTrack = null;
        Refresh();
    }

    private void ToggleFavorite()
    {
        if (SelectedTrack == null) return;
        _library.ToggleFavorite(SelectedTrack.Id);
        Refresh();
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
        Refresh();
    }
}
