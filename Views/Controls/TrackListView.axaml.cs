using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using NullWave.Models;
using NullWave.Services;
using NullWave.ViewModels;

namespace NullWave.Views.Controls;

public partial class TrackListView : DockPanel
{
    public TrackListView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        // Wire up container realization counter for DevTools
        TrackList.ContainerPrepared -= OnContainerPrepared;
        TrackList.ContainerPrepared += OnContainerPrepared;
    }

    private void OnContainerPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        var root = TopLevel.GetTopLevel(this)?.DataContext as MainViewModel;
        root?.Settings?.IncrementRealizationCount();
    }

    // 
    // LAZY ROW MENUS
    // Menus are built at open time (a user-initiated, once-per-click event)
    // instead of being inflated per container during materialization.
    // Right-click: ContextRequested is a routed event, so one handler at the
    // ListBox level covers every row. Dots button: plain Click handler
    // replaces the per-row Button.Flyout.
    // 

    private void OnListContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not ListBox list) return;
        var track = FindRowTrack(e.Source as Visual);
        if (track == null) return; // right-click on empty area/header: no menu, as before
        e.Handled = true;
        BuildRowMenu(track, includeMediaToggles: true).ShowAt(list, true); // at pointer, like a ContextMenu
    }

    private void OnRowMenuClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        var track = FindRowTrack(btn);
        if (track == null) return;
        BuildRowMenu(track, includeMediaToggles: false).ShowAt(btn); // default placement, like the old Flyout
    }

    private static Track? FindRowTrack(Visual? source)
    {
        var v = source;
        while (v != null)
        {
            if (v.DataContext is Track t) return t;
            v = v.GetVisualParent();
        }
        return null;
    }

    private MenuFlyout BuildRowMenu(Track track, bool includeMediaToggles)
    {
        var menu = new MenuFlyout();
        if (DataContext is not LibraryViewModel vm) return menu;
        var root = TopLevel.GetTopLevel(this)?.DataContext as MainViewModel;

        AddItem(menu, LocalizationService.Instance["ContextMenu_Play"], vm.PlayTrackCommand, track);
        AddItem(menu, LocalizationService.Instance["ContextMenu_ViewDetails"], vm.OpenDetailCommand, null);
        AddItem(menu, LocalizationService.Instance["ContextMenu_ToggleFavorite"], vm.ToggleFavoriteCommand, track);
        AddItem(menu, LocalizationService.Instance["ContextMenu_AddToQueue"], vm.AddToQueueCommand, track);
        if (root != null)
            AddItem(menu, LocalizationService.Instance["ContextMenu_AddToPlaylist"], root.AddTrackToPlaylistCommand, track);
        AddItem(menu, LocalizationService.Instance["ContextMenu_CopyUrl"], vm.CopyUrlCommand, null);
        menu.Items.Add(new Separator());

        if (includeMediaToggles)
        {
            AddItem(menu, "Mark as Audiobook", vm.MarkAsAudiobookCommand, null);
            AddItem(menu, "Mark as Music", vm.MarkAsMusicCommand, null);
            menu.Items.Add(new Separator());
        }

        AddItem(menu, LocalizationService.Instance["ContextMenu_Remove"], vm.RemoveTrackCommand, track);
        return menu;
    }

    private static void AddItem(MenuFlyout menu, string header, ICommand command, object? parameter)
        => menu.Items.Add(new MenuItem { Header = header, Command = command, CommandParameter = parameter });

    // Existing handlers (unchanged)

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

    private void OnClearSelection(object? sender, RoutedEventArgs e)
    {
        TrackList?.SelectedItems?.Clear();
    }

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

            // FIX (Avalonia 12 Breaking Change): IClipboard no longer exposes text methods directly.
            // We must request an IAsyncDataTransfer object, ensure it is disposed, and then 
            // use the AsyncDataTransferExtensions.TryGetTextAsync() extension method.
            using var data = await clipboard.TryGetDataAsync();
            if (data == null) return;

            var text = await data.TryGetTextAsync();
            if (string.IsNullOrWhiteSpace(text)) return;

            text = text.Trim();
            vm.Input.InputUrl = text;

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