using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using NullWave.Helpers;
using NullWave.Models;
using NullWave.Plugins.Contracts;
using NullWave.Services;

namespace NullWave.Views.Dialogs;

public partial class RadioCatalogDialog : Window
{
    private readonly IRadioProvider? _radioProvider;
    private readonly Func<RadioStationInfo, bool>? _tryAdd;
    private CancellationTokenSource? _searchCts;

    public RadioCatalogDialog()
    {
        InitializeComponent();
    }

    public RadioCatalogDialog(IRadioProvider? radioProvider, Func<RadioStationInfo, bool> tryAdd) : this()
    {
        _radioProvider = radioProvider;
        _tryAdd = tryAdd;
        
        // Initial load: Top stations
        _ = LoadStationsAsync(null);
    }

    private async void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is TextBox tb)
        {
            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();
            var token = _searchCts.Token;
            
            // Debounce 300ms
            try { await Task.Delay(300, token); } catch (TaskCanceledException) { return; }
            
            await LoadStationsAsync(tb.Text, token);
        }
    }

    private async Task LoadStationsAsync(string? query, CancellationToken ct = default)
    {
        var featured = string.IsNullOrWhiteSpace(query)
            ? RadioStationCatalog.GetCuratedStations()
                .Select(station => new RadioStationInfo(
                    "curated:" + station.Name,
                    station.Name,
                    station.Genre,
                    null,
                    null,
                    station.StreamUrl,
                    null))
                .ToList()
            : new List<RadioStationInfo>();

        if (_radioProvider == null)
        {
            StationList.ItemsSource = featured;
            return;
        }

        try
        {
            var stations = string.IsNullOrWhiteSpace(query)
                ? await _radioProvider.GetTopStationsAsync(50, ct)
                : await _radioProvider.SearchStationsAsync(query, 50, ct);
                
            if (!ct.IsCancellationRequested)
                StationList.ItemsSource = featured.Concat(stations).ToList();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            ToastService.Instance.Show($"Search failed: {ex.Message}", ToastType.Error);
            StationList.ItemsSource = featured;
        }
    }

    private async void OnAddStationClicked(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is RadioStationInfo station && _tryAdd != null)
        {
            if (sender is Button btn) btn.IsEnabled = false;
            
            var streamUrl = station.StreamUrl;
            if (string.IsNullOrWhiteSpace(streamUrl) && _radioProvider != null)
            {
                streamUrl = await _radioProvider.ResolveStreamUrlAsync(station.Id);
            }

            if (string.IsNullOrWhiteSpace(streamUrl))
            {
                ToastService.Instance.Show($"Could not resolve stream for '{station.Name}'.", ToastType.Warning);
                if (sender is Button b2) b2.IsEnabled = true;
                return;
            }

            // Pass the resolved URL back to the callback
            bool added = _tryAdd(station with { StreamUrl = streamUrl });
            
            if (added)
            {
                ToastService.Instance.Show($"Added '{station.Name}' to Radio library.", ToastType.Success, scope: "radio-add");
            }
            else
            {
                ToastService.Instance.Show($"'{station.Name}' is already in your library.", ToastType.Info, scope: "radio-add");
                if (sender is Button b3) b3.IsEnabled = true;
            }
        }
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e) => Close();
}