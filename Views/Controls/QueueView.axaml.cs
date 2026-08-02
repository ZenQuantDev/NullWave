using System;
using Avalonia.Controls;
using Avalonia.Input;
using NullWave.Models;
using NullWave.ViewModels;

namespace NullWave.Views.Controls;

public partial class QueueView : UserControl
{
    private static readonly DataFormat<QueueEntry> QueueEntryFormat =
        DataFormat.CreateInProcessFormat<QueueEntry>("QueueEntry");

    public QueueView()
    {
        InitializeComponent();
    }

    private void OnTrackDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control control && control.Tag is QueueEntry entry)
        {
            if (DataContext is MainViewModel vm)
                vm.Queue.PlayNowCommand.Execute(entry.Track);
        }
    }

    private async void OnQueueItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control control && control.Tag is QueueEntry entry)
        {
            var dataTransfer = new DataTransfer();
            dataTransfer.Add(DataTransferItem.Create(QueueEntryFormat, entry));

            await DragDrop.DoDragDropAsync(e, dataTransfer, DragDropEffects.Move);
        }
    }

    private void OnQueueItemDragEnter(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(QueueEntryFormat)
            ? DragDropEffects.Move
            : DragDropEffects.None;
    }

    private void OnQueueItemDragLeave(object? sender, DragEventArgs e)
    {
        e.DragEffects = DragDropEffects.None;
    }

    private void OnQueueItemDrop(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.TryGetValue(QueueEntryFormat) is not { } entry) return;
        if (sender is not Control control || control.Tag is not QueueEntry targetEntry) return;
        if (DataContext is not MainViewModel vm) return;

        var current = vm.Queue.Tracks;
        var newIndex = current.IndexOf(targetEntry);
        vm.Queue.MoveTrackTo(entry.Track, newIndex);
    }
}