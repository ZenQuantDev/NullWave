using System;
using System.Linq;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NullWave.Models;
using NullWave.Services;
using NullWave.ViewModels;
using NullWave.Models.FileSystem;
using Serilog;

namespace NullWave.Views;

public partial class FileBrowserView : UserControl
{
    public FileBrowserView() => InitializeComponent();

    protected override async void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is not FileBrowserViewModel vm) return;
        vm.PropertyChanged += OnVmPropertyChanged;
        vm.ClearSelectionRequested += OnClearSelectionRequested;
        try { await vm.EnsureInitializedAsync(); vm.StartDriveWatch(); }
        catch (Exception ex) { Log.Error(ex, "[FileBrowser] Initialization failed"); }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is FileBrowserViewModel vm)
        {
            vm.PropertyChanged -= OnVmPropertyChanged;
            vm.ClearSelectionRequested -= OnClearSelectionRequested;
            vm.StopDriveWatch();
        }
        base.OnDetachedFromVisualTree(e);
    }

    private void OnClearSelectionRequested() => FileList.UnselectAll();

    private void OnListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox lb && DataContext is FileBrowserViewModel vm)
        {
            var selected = lb.SelectedItems?.OfType<FileSystemNode>() ?? Enumerable.Empty<FileSystemNode>();
            vm.UpdateSelection(selected);
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (DataContext is not FileBrowserViewModel vm) return;

        if (e.PropertyName == nameof(FileBrowserViewModel.CurrentNodes))
        {
            if (EffectsTierResolver.Shared.Current == EffectsTier.Minimal) return;
            var t = ListingArea.Transitions;
            ListingArea.Transitions = null;
            ListingArea.Opacity = 0;
            Dispatcher.UIThread.Post(() => { ListingArea.Transitions = t; ListingArea.Opacity = 1; }, DispatcherPriority.Render);
        }
        else if (e.PropertyName == nameof(FileBrowserViewModel.IsEditingPath) && vm.IsEditingPath)
        {
            Dispatcher.UIThread.Post(() => { PathBox.Focus(); PathBox.SelectAll(); }, DispatcherPriority.Input);
        }
        else if (e.PropertyName == nameof(FileBrowserViewModel.CurrentPath))
        {
            Dispatcher.UIThread.Post(() =>
            {
                BreadcrumbScroll.ScrollToEnd();
                BringActivePlaceIntoView();
            }, DispatcherPriority.Background);
        }
    }

    private void BringActivePlaceIntoView()
    {
        if (DataContext is not FileBrowserViewModel vm) return;
        for (int i = 0; i < vm.Places.Count; i++)
        {
            if (!vm.Places[i].IsCurrentPlace) continue;
            if (PlacesList.ContainerFromIndex(i) is Control c) c.BringIntoView();
            return;
        }
    }

    private void OnListDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Visual v && v.FindAncestorOfType<Button>() != null) return;
        if (DataContext is FileBrowserViewModel vm) vm.OpenFileCommand.Execute(vm.SelectedNode);
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not FileBrowserViewModel vm) return;
        switch (e.Key)
        {
            case Key.Enter: vm.OpenFileCommand.Execute(vm.SelectedNode); e.Handled = true; break;
            case Key.F5: vm.RefreshCommand.Execute(null); e.Handled = true; break;
        }
    }

    private void OnPathEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not FileBrowserViewModel vm) return;
        if (e.Key == Key.Enter) { vm.CommitEditPathCommand.Execute(null); e.Handled = true; }
        else if (e.Key == Key.Escape) { vm.CancelEditPathCommand.Execute(null); e.Handled = true; }
    }

    private void OnPathEditorLostFocus(object? s, RoutedEventArgs e)
    {
        if (DataContext is FileBrowserViewModel vm) vm.CancelEditPathCommand.Execute(null);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (DataContext is not FileBrowserViewModel vm) return;
        if (e.Source is TextBox) return;

        bool alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);

        if (e.Key == Key.Back && !ctrl && !alt && vm.CanGoUp)
        {
            vm.GoUpCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (alt && e.Key == Key.Left && vm.CanGoBack) { vm.GoBackCommand.Execute(null); e.Handled = true; }
        else if (alt && e.Key == Key.Right && vm.CanGoForward) { vm.GoForwardCommand.Execute(null); e.Handled = true; }
        else if (alt && e.Key == Key.Up && vm.CanGoUp) { vm.GoUpCommand.Execute(null); e.Handled = true; }
        else if (ctrl && e.Key == Key.L) { vm.StartEditPathCommand.Execute(null); e.Handled = true; }
    }
}