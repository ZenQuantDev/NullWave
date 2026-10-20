using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using NullWave.Helpers;
using NullWave.Models;
using NullWave.Services;

namespace NullWave.ViewModels;

public partial class DownloadManagerViewModel : ObservableObject
{
    // We access the singleton/static instance or inject it. 
    // Assuming DownloadService is accessible via MainViewModel or a static locator.
    // For simplicity, we'll bind directly to the ActiveJobs collection.
    
    public ObservableCollection<DownloadJob> Jobs { get; } = new();
    public ICommand RetryCommand { get; }
    public ICommand ClearCompletedCommand { get; }
    
    public bool HasJobs => Jobs.Count > 0;

    public DownloadManagerViewModel()
    {
        RetryCommand = new RelayCommand<DownloadJob>(job => job?.RetryAction?.Invoke());
        ClearCompletedCommand = new RelayCommand(() =>
        {
            for (int i = Jobs.Count - 1; i >= 0; i--)
                if (Jobs[i].IsCompleted || Jobs[i].IsFailed) Jobs.RemoveAt(i);
            OnPropertyChanged(nameof(HasJobs));
        });
        // In a real DI setup, inject DownloadService. 
        // For now, we'll hook into the event or just expose the collection.
        // We will wire this up in MainViewModel.
    }

    public void AttachService(DownloadService service)
    {
        Jobs.Clear();
        foreach(var j in service.ActiveJobs) Jobs.Add(j);
        service.ActiveJobs.CollectionChanged += (s, e) => 
        {
            if (e.NewItems != null) foreach (DownloadJob j in e.NewItems) Jobs.Add(j);
            if (e.OldItems != null) foreach (DownloadJob j in e.OldItems) Jobs.Remove(j);
            OnPropertyChanged(nameof(HasJobs));
        };
    }
}