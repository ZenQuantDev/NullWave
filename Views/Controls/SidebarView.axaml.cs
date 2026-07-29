using Avalonia.Controls;
using Avalonia.Input;
using NullWave.Models;
using NullWave.ViewModels;
using System.ComponentModel;

namespace NullWave.Views.Controls;

public partial class SidebarView : Border
{
    private static readonly DataFormat<NavItem> NavItemFormat =
        DataFormat.CreateInProcessFormat<NavItem>("nullwave-navitem");

    public SidebarView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.PropertyChanged += OnViewModelPropertyChanged;
            vm.Library.PropertyChanged += OnLibraryPropertyChanged;
            UpdateButtonClasses(vm);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CurrentPage) && DataContext is MainViewModel vm)
        {
            UpdateButtonClasses(vm);
        }
    }

    private void OnLibraryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        switch (e.PropertyName)
        {
            case nameof(LibraryViewModel.IsFavoritesView):
            case nameof(LibraryViewModel.IsRecentView):
            case nameof(LibraryViewModel.IsYouTubeFilter):
            case nameof(LibraryViewModel.IsLastFmFilter):
            case nameof(LibraryViewModel.IsSoundCloudFilter):
            case nameof(LibraryViewModel.IsLocalFilter):
                UpdateButtonClasses(vm);
                break;
        }
    }

    private void UpdateButtonClasses(MainViewModel vm)
    {
        var isOnLibrary = vm.CurrentPage == "Library";
        SetActive(FavBtn, isOnLibrary && vm.Library.IsFavoritesView);
        SetActive(RecentBtn, isOnLibrary && vm.Library.IsRecentView);
        SetActive(YTBtn, isOnLibrary && vm.Library.IsYouTubeFilter);
        SetActive(LFMBtn, isOnLibrary && vm.Library.IsLastFmFilter);
        SetActive(SCBtn, isOnLibrary && vm.Library.IsSoundCloudFilter);
        SetActive(LocalBtn, isOnLibrary && vm.Library.IsLocalFilter);
    }

    private void SetActive(Button? btn, bool isActive)
    {
        if (btn == null) return;

        if (isActive)
        {
            if (!btn.Classes.Contains("active"))
                btn.Classes.Add("active");
        }
        else
        {
            btn.Classes.Remove("active");
        }
    }

    private async void OnNavItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || !vm.IsCustomizingSidebar) return;
        if (sender is not Border handle || handle.Tag is not NavItem item) return;
        if (!e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed) return;

        var dataItem = new DataTransferItem();
        dataItem.Set(NavItemFormat, item);

        var data = new DataTransfer();
        data.Add(dataItem);

        item.IsDragging = true;
        try
        {
            await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move);
        }
        finally
        {
            item.IsDragging = false;
            foreach (var navItem in vm.Nav.Items) navItem.IsDropTarget = false;
        }
    }

    private void OnNavItemDragEnter(object? sender, DragEventArgs e)
    {
        if (sender is Border targetBorder && targetBorder.Tag is NavItem targetItem)
            targetItem.IsDropTarget = true;
    }

    private void OnNavItemDragLeave(object? sender, DragEventArgs e)
    {
        if (sender is Border targetBorder && targetBorder.Tag is NavItem targetItem)
            targetItem.IsDropTarget = false;
    }

    private void OnNavItemDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (sender is not Border targetBorder || targetBorder.Tag is not NavItem targetItem) return;

        targetItem.IsDropTarget = false;
        var draggedItem = e.DataTransfer.TryGetValue(NavItemFormat);
        if (draggedItem is null || draggedItem == targetItem) return;

        var newIndex = vm.Nav.Items.IndexOf(targetItem);
        vm.Nav.MoveItem(draggedItem, newIndex);
    }
}