using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Input;
using Avalonia.Threading;
using NullWave.Helpers;
using NullWave.Models;
using NullWave.Services;
using NullWave.ViewModels.Base;

namespace NullWave.ViewModels;

public class QueueViewModel : ViewModelBase
{
    private readonly LibraryService _library;
    private bool _isOpen;
    private int _refreshPending;

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
    public bool HasTracks => Tracks.Count > 0;

    public BulkObservableCollection<QueueEntry> Tracks { get; } = new();

    public ICommand CloseCommand { get; }
    public ICommand ClearQueueCommand { get; }
    public ICommand RemoveFromQueueCommand { get; }
    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }
    public ICommand PlayNowCommand { get; }

    public event Action<Track>? PlayTrackRequested;
    public event Action<Track>? TrackDetailRequested;

    public void OpenTrackDetail(Track track) => TrackDetailRequested?.Invoke(track);

    public QueueViewModel(LibraryService library)
    {
        _library = library;
        _library.QueueChanged += (_, _) => ScheduleRefresh();
        Refresh();

        CloseCommand = new RelayCommand(() => IsOpen = false);
        ClearQueueCommand = new RelayCommand(() =>
        {
            var snapshot = _library.GetQueue().ToList();
            if (snapshot.Count == 0) return;
            _library.ClearQueue();
            ToastService.Instance.Show(
                message: $"Cleared {snapshot.Count} item(s) from queue.",
                type: ToastType.Warning,
                durationMs: 6000,
                actionText: "Undo",
                actionCallback: () => _library.RestoreQueue(snapshot),
                scope: "queue-clear");
        });

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

    private void ScheduleRefresh()
    {
        if (Interlocked.Exchange(ref _refreshPending, 1) == 1) return;
        Dispatcher.UIThread.Post(() => { Volatile.Write(ref _refreshPending, 0); Refresh(); }, DispatcherPriority.Background);
    }

    public void MoveTrackTo(Track track, int newIndex)
    {
        var current = _library.GetQueue().ToList();
        var oldIndex = current.FindIndex(e => e.Track.Id == track.Id);
        if (oldIndex < 0 || newIndex < 0 || newIndex >= current.Count || oldIndex == newIndex) return;
        _library.MoveQueueItem(oldIndex, newIndex);
    }

    private void Refresh()
    {
        Tracks.ReplaceAll(_library.GetQueue());
        OnPropertyChanged(nameof(HasTracks));
    }

    private void Move(Track? track, int delta)
    {
        if (track == null) return;
        var current = _library.GetQueue().ToList();
        var index = current.FindIndex(e => e.Track.Id == track.Id);
        if (index < 0) return;
        _library.MoveQueueItem(index, index + delta);
    }
}