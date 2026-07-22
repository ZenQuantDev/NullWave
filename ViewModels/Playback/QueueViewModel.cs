using System;
using System.Linq;
using System.Windows.Input;
using NullWave.Helpers;
using NullWave.Models;
using NullWave.Services;
using NullWave.ViewModels.Base;

namespace NullWave.ViewModels;

public class QueueViewModel : ViewModelBase
{
    private readonly LibraryService _library;
    private bool _isOpen;

    public bool IsOpen
    {
        get => _isOpen;
        set
        {
            _isOpen = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PanelWidth));
            OnPropertyChanged(nameof(PanelOpacity));
        }
    }

    public double PanelWidth => _isOpen ? 320 : 0;
    public double PanelOpacity => _isOpen ? 1.0 : 0.0;

    public BulkObservableCollection<Track> Tracks { get; } = new();

    public ICommand CloseCommand { get; }
    public ICommand ClearQueueCommand { get; }
    public ICommand RemoveFromQueueCommand { get; }
    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }
    public ICommand PlayNowCommand { get; }

    public event Action<Track>? PlayTrackRequested;

    public QueueViewModel(LibraryService library)
    {
        _library = library;
        _library.QueueChanged += (_, _) => Refresh();
        Refresh();

        CloseCommand = new RelayCommand(() => IsOpen = false);
        ClearQueueCommand = new RelayCommand(() => _library.ClearQueue());
        RemoveFromQueueCommand = new RelayCommand<Track>(t =>
        {
            if (t != null) _library.RemoveFromQueue(t.Id);
        });
        MoveUpCommand = new RelayCommand<Track>(t => Move(t, -1));
        MoveDownCommand = new RelayCommand<Track>(t => Move(t, 1));
        PlayNowCommand = new RelayCommand<Track>(t =>
        {
            if (t != null) PlayTrackRequested?.Invoke(t);
        });
    }

    private void Refresh()
    {
        Tracks.ReplaceAll(_library.GetQueue());
    }

    private void Move(Track? track, int delta)
    {
        if (track == null) return;
        var current = _library.GetQueue().ToList();
        var index = current.FindIndex(t => t.Id == track.Id);
        if (index < 0) return;
        _library.MoveQueueItem(index, index + delta);
    }
}