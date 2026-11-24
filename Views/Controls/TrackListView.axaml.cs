using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NullWave.Models;
using NullWave.Services;
using NullWave.ViewModels;
using NullWave.Helpers;

namespace NullWave.Views.Controls;

public partial class TrackListView : DockPanel
{
    private LibraryViewModel? _watchedVm;
    private int _fadeSeq = -1;
    private bool _fadingOut, _fadingIn;
    private int FadeMs => this.FindResource("DurationDip") is TimeSpan d ? (int)d.TotalMilliseconds : 10;

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

        // (Re)bind the dip to whichever Library VM this view now shows.
        if (_watchedVm != null) _watchedVm.PropertyChanged -= OnVmPropertyChanged;
        _watchedVm = DataContext as LibraryViewModel;
        if (_watchedVm != null)
        {
            _watchedVm.ForceResolvePendingFade();   // heal a dip orphaned by a previous detach
            _watchedVm.PropertyChanged += OnVmPropertyChanged;
        }

        // No self-entrance fade: the page crossfade (PageHost) owns surface entry, exactly like
        // Files after 3b-ii. Snap the list fully visible so a re-attached view never holds a stale dip.
        _fadingOut = false; _fadingIn = false; _fadeSeq = -1;
        TrackList.Opacity = 1;
    }

    // A newer switch superseded us mid fade-out (IsListFadingOut was already true, so no rising edge
    // fired StartFadeOut). Re-arm the fade-out leg for the new seq: snap visible WITHOUT transition
    // (kills the in-flight 1->0), then fade out again, and schedule the trough against the NEW seq so
    // we never strand opacity at 0. The old leg's callback still fires and bails on seq mismatch.
    private void RestartFadeOutLeg(LibraryViewModel vm)
    {
        _fadeSeq = vm.ListSwitchSeq;
        var t = TrackList.Transitions;
        TrackList.Transitions = null;
        TrackList.Opacity = 1;          // instant reset
        TrackList.Transitions = t;
        TrackList.Opacity = 0.25;          // fresh fade-out over DurationPage
        var mySeq = _fadeSeq;
        ScheduleAfterFade(() =>
        {
            if (mySeq != vm.ListSwitchSeq) return;                 // superseded again -> newest leg owns it
            if (_fadingOut && vm.IsListFadingOut) vm.CompleteListFadeOut(mySeq);
            else SnapVisible();                                    // aborted by an instant refresh
        });
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not LibraryViewModel vm) return;
        switch (e.PropertyName)
        {
            case nameof(LibraryViewModel.IsListFadingOut): if (vm.IsListFadingOut) StartFadeOut(vm); break;
            case nameof(LibraryViewModel.IsListFadingIn):  if (vm.IsListFadingIn)  StartFadeIn(vm);  break;
            case nameof(LibraryViewModel.ListSwitchSeq):
                if (_fadingOut && _fadeSeq != vm.ListSwitchSeq) RestartFadeOutLeg(vm); // newer switch superseded mid fade-out
                break;
        }
    }

    // Leg 1: fade the CURRENT (old) list 1 -> 0 over DurationPage. At the trough we hand control
    // back to the VM (CompleteListFadeOut) which swaps the data while opacity is 0 and raises leg 2.
    private void StartFadeOut(LibraryViewModel vm)
    {
        var mySeq = vm.ListSwitchSeq; _fadeSeq = mySeq;
        _fadingOut = true; _fadingIn = false;
        if (EffectsTierResolver.Shared.Current == EffectsTier.Minimal) { vm.CompleteListFadeOut(mySeq); return; }

        TrackList.Opacity = 0.25; // transitions active (see B3) => animates 1 -> 0
        ScheduleAfterFade(() =>
        {
            if (mySeq != vm.ListSwitchSeq) return;                 // superseded by a newer dip -> that dip owns opacity
            if (_fadingOut && vm.IsListFadingOut) vm.CompleteListFadeOut(mySeq); // normal: swap at trough + raise leg 2
            else SnapVisible();                                    // aborted by an instant refresh -> show new data now
        });
    }

    // Leg 2: fade the NEW list 0 -> 1 over DurationPage, then clear the flag so the next switch re-arms.
    private void StartFadeIn(LibraryViewModel vm)
    {
        var mySeq = vm.ListSwitchSeq;
        _fadingIn = true; _fadingOut = false;
        if (EffectsTierResolver.Shared.Current == EffectsTier.Minimal) { vm.CompleteListFadeIn(mySeq); return; }

        TrackList.Opacity = 1; // from 0 => animates 0 -> 1
        ScheduleAfterFade(() =>
        {
            if (mySeq != vm.ListSwitchSeq) return;                 // superseded
            if (_fadingIn && vm.IsListFadingIn) vm.CompleteListFadeIn(mySeq);
            // else: aborted mid-fade-in; opacity is already animating to 1, nothing to do.
        });
    }

    private void SnapVisible() { _fadingOut = false; _fadingIn = false; TrackList.Opacity = 1; }

    private void ScheduleAfterFade(Action onUi)
    {
        var ms = FadeMs;
        _ = Task.Run(async () => { await Task.Delay(ms); Dispatcher.UIThread.Post(onUi); });
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
}