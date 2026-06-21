using Avalonia.Controls;
using NullWave.ViewModels;
using System.ComponentModel;
using Serilog;

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
        Log.Information("[SidebarView] DIAGNOSTIC: DataContextChanged fired. DataContext is now {Type}",
            DataContext?.GetType().FullName ?? "null");

        if (DataContext is MainViewModel vm)
        {
            vm.PropertyChanged += OnViewModelPropertyChanged;
            vm.Library.PropertyChanged += OnLibraryPropertyChanged;
            Log.Information("[SidebarView] DIAGNOSTIC: Subscribed to vm.PropertyChanged and vm.Library.PropertyChanged. Library instance hash={Hash}",
                vm.Library.GetHashCode());
            UpdateButtonClasses(vm);
        }
        else
        {
            Log.Warning("[SidebarView] DIAGNOSTIC: DataContext is NOT MainViewModel — subscriptions skipped!");
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        Log.Information("[SidebarView] DIAGNOSTIC: MainViewModel.PropertyChanged fired for {Prop}", e.PropertyName);

        if (e.PropertyName == nameof(MainViewModel.CurrentPage) && DataContext is MainViewModel vm)
        {
            UpdateButtonClasses(vm);
        }
    }

    private void OnLibraryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        Log.Information("[SidebarView] DIAGNOSTIC: Library.PropertyChanged fired for {Prop} (sender hash={Hash})",
            e.PropertyName, sender?.GetHashCode());

        if (DataContext is not MainViewModel vm)
        {
            Log.Warning("[SidebarView] DIAGNOSTIC: DataContext is not MainViewModel inside OnLibraryPropertyChanged!");
            return;
        }

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
        Log.Information("[SidebarView] DIAGNOSTIC: UpdateButtonClasses called. CurrentPage={Page}, YT={YT}, SC={SC}, LFM={LFM}, Local={Local}, Fav={Fav}, Recent={Recent}",
            vm.CurrentPage, vm.Library.IsYouTubeFilter, vm.Library.IsSoundCloudFilter,
            vm.Library.IsLastFmFilter, vm.Library.IsLocalFilter,
            vm.Library.IsFavoritesView, vm.Library.IsRecentView);

        SetActive(LibBtn, vm.CurrentPage == "Library");
        SetActive(PlBtn, vm.CurrentPage == "Playlists");
        SetActive(QueueBtn, vm.CurrentPage == "Queue");
        SetActive(StatsBtn, vm.CurrentPage == "Stats");

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
        if (btn == null)
        {
            Log.Warning("[SidebarView] DIAGNOSTIC: SetActive called with NULL button reference!");
            return;
        }

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