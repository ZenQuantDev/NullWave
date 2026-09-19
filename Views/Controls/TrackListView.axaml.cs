using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using NullWave.Models;
using NullWave.ViewModels;

namespace NullWave.Views.Controls;

public partial class TrackListView : DockPanel
{
    public TrackListView()
    {
        InitializeComponent();
    }

    private void OnTrackSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not LibraryViewModel vm) return;

        var selected = (sender as ListBox)?.SelectedItems?.OfType<Track>().ToList()
                       ?? new List<Track>();
        vm.UpdateSelection(selected);

        if (selected.Count == 1 && TopLevel.GetTopLevel(this)?.DataContext is MainViewModel root)
        {
            root.Detail.OpenFor(selected[0]);
        }
    }

    private void OnTrackDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is LibraryViewModel vm && vm.SelectedTrack != null)
        {
            vm.PlayTrackCommand.Execute(vm.SelectedTrack);
        }
    }

    private void OnClearSelection(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        TrackList?.SelectedItems?.Clear();
    }

    // Auto-fills the "Add Track" input box if a valid URL is currently in the clipboard
        // Auto-fills the "Add Track" input box if a valid URL is currently in the clipboard
        // Auto-fills the "Add Track" input box if a valid URL is currently in the clipboard
    public async void OnAddFlyoutOpened(object? sender, EventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (!string.IsNullOrWhiteSpace(vm.Input.InputUrl)) return;

        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            var clipboard = topLevel?.Clipboard;
            if (clipboard == null) return;

            // TryGetTextAsync is the modern Avalonia 11.2+/12 extension method for reading text
            var text = await clipboard.TryGetTextAsync();
            if (string.IsNullOrWhiteSpace(text)) return;

            text = text.Trim();
            vm.Input.InputUrl = text;

            // If the pasted text isn't a valid URL, clear it immediately
            if (!vm.Input.IsInputUrlValid)
            {
                vm.Input.InputUrl = string.Empty;
            }
        }
        catch
        {
            // Clipboard access can fail (e.g., locked by another app) - silently skip pre-fill.
        }
    }
}