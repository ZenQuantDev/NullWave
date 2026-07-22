using Avalonia.Controls;
using NullWave.ViewModels;
using System.ComponentModel;

namespace NullWave.Views.Controls;

public partial class SidebarView : Border
{
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
        // Core nav items are now data-driven via NavItem.IsActive, so we only manage Filters/Sources here
        
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
}