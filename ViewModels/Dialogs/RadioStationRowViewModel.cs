using CommunityToolkit.Mvvm.ComponentModel;
using NullWave.Plugins.Contracts;

namespace NullWave.ViewModels.Dialogs;

/// <summary>
/// UI row for the radio catalog. Keeps library state (IsAdded) out of the
/// plugin contract record, which must stay pure data.
/// </summary>
public partial class RadioStationRowViewModel : ObservableObject
{
    public RadioStationInfo Station { get; private set; }

    public RadioStationRowViewModel(RadioStationInfo station, bool isAdded)
    {
        Station = station;
        _isAdded = isAdded;
    }

    [ObservableProperty] private bool _isAdded;
    [ObservableProperty] private bool _isBusy;

    public string Id => Station.Id;
    public string Name => Station.Name;
    public string? Genre => Station.Genre;
    public string? Country => Station.Country;
    public int ClickCount => Station.ClickCount;
    public string? StreamUrl => Station.StreamUrl;

    public string ActionLabel => IsAdded ? "Added" : "Add";
    public bool CanAdd => !IsAdded && !IsBusy;

    partial void OnIsAddedChanged(bool value)
    {
        OnPropertyChanged(nameof(ActionLabel));
        OnPropertyChanged(nameof(CanAdd));
    }

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanAdd));
}