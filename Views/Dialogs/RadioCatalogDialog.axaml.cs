using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using NullWave.Helpers;
using NullWave.Services; 
using NullWave.Models;

namespace NullWave.Views.Dialogs;

public partial class RadioCatalogDialog : Window
{
    private readonly Func<CuratedStation, bool>? _tryAdd;

    public RadioCatalogDialog()
    {
        InitializeComponent();
        StationList.ItemsSource = RadioStationCatalog.GetCuratedStations();
    }

    public RadioCatalogDialog(Func<CuratedStation, bool> tryAdd) : this()
    {
        _tryAdd = tryAdd;
    }

    private void OnAddStationClicked(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is CuratedStation station)
        {
            if (_tryAdd != null)
            {
                bool added = _tryAdd(station);
                if (added)
                {
                    ToastService.Instance.Show($"Added '{station.Name}' to Radio library.", ToastType.Success, scope: "radio-add");
                    // Disable the button to prevent double-adds
                    if (sender is Button btn) btn.IsEnabled = false;
                }
                else
                {
                    ToastService.Instance.Show($"'{station.Name}' is already in your library.", ToastType.Info, scope: "radio-add");
                }
            }
        }
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e) => Close();
}