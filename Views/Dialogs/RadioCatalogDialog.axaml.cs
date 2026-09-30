using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using NullWave.Helpers;
using NullWave.Services; 
using NullWave.Models;

namespace NullWave.Views.Dialogs;

public partial class RadioCatalogDialog : Window
{
    public List<CuratedStation> AddedStations { get; } = new();

    public RadioCatalogDialog()
    {
        InitializeComponent();
        StationList.ItemsSource = RadioStationCatalog.GetCuratedStations();
    }

    private void OnAddStationClicked(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is CuratedStation station)
        {
            if (!AddedStations.Contains(station))
            {
                AddedStations.Add(station);
                ToastService.Instance.Show($"Added '{station.Name}' to queue.", ToastType.Success, scope: "radio-add");
            }
            else
            {
                ToastService.Instance.Show($"'{station.Name}' is already in the add list.", ToastType.Info, scope: "radio-add");
            }
        }
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e) => Close(AddedStations);
}