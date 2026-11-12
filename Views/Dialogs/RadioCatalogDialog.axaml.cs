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
using NullWave.ViewModels.Dialogs;

namespace NullWave.Views.Dialogs;

public partial class RadioCatalogDialog : Window
{
    private readonly IRadioProvider? _radioProvider;
    private readonly Func<RadioStationInfo, bool>? _tryAdd;
    private readonly Func<RadioStationInfo, bool>? _isAlreadyAdded;
    private CancellationTokenSource? _searchCts;

    public RadioCatalogDialog()
    {
        InitializeComponent();
        Closed += (_, _) => DisposeStationRows();
    }

    public RadioCatalogDialog(
        IRadioProvider? radioProvider,
        Func<RadioStationInfo, bool> tryAdd,
        Func<RadioStationInfo, bool> isAlreadyAdded) : this()
    {
        _radioProvider = radioProvider;
        _tryAdd = tryAdd;
        _isAlreadyAdded = isAlreadyAdded;

        _ = LoadStationsAsync(null);
    }

    private async void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox tb) return;

        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        try { await Task.Delay(300, token); } catch (TaskCanceledException) { return; }
        await LoadStationsAsync(tb.Text, token);
    }

    private async Task LoadStationsAsync(string? query, CancellationToken ct = default)
    {
        var featured = string.IsNullOrWhiteSpace(query)
            ? RadioStationCatalog.GetCuratedStations()
                .Select(s => new RadioStationInfo("curated:" + s.Name, s.Name, s.Genre, null, null, s.StreamUrl, null))
                .ToList()
            : new List<RadioStationInfo>();

        List<RadioStationInfo> stations;
        if (_radioProvider == null)
        {
            stations = featured;
        }
        else
        {
            try
            {
                var live = string.IsNullOrWhiteSpace(query)
                    ? await _radioProvider.GetTopStationsAsync(50, ct)
                    : await _radioProvider.SearchStationsAsync(query, 50, ct);
                stations = featured.Concat(live).ToList();
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                ToastService.Instance.Show($"Search failed: {ex.Message}", ToastType.Error);
                stations = featured;
            }
        }

        if (ct.IsCancellationRequested) return;

        SetStationRows(stations
            .Select(s => new RadioStationRowViewModel(s, _isAlreadyAdded?.Invoke(s) ?? false))
            .ToList());
    }

    private void SetStationRows(List<RadioStationRowViewModel> rows)
    {
        DisposeStationRows();
        StationList.ItemsSource = rows;
    }

    private void DisposeStationRows()
    {
        if (StationList.ItemsSource is IEnumerable<RadioStationRowViewModel> rows)
        {
            foreach (var row in rows)
                row.Dispose();
        }
    }

    private async void OnAddStationClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: RadioStationRowViewModel row } || _tryAdd == null) return;

        row.IsBusy = true;
        try
        {
            var streamUrl = row.StreamUrl;
            if (string.IsNullOrWhiteSpace(streamUrl) && _radioProvider != null)
                streamUrl = await _radioProvider.ResolveStreamUrlAsync(row.Id);

            if (string.IsNullOrWhiteSpace(streamUrl))
            {
                ToastService.Instance.Show($"Could not resolve stream for '{row.Name}'.", ToastType.Warning);
                return;
            }

            var added = _tryAdd(row.Station with { StreamUrl = streamUrl });

            // Either we just added it, or it was already there. Both mean "in library".
            row.IsAdded = true;
            ToastService.Instance.Show(
                added ? $"Added '{row.Name}' to Radio library." : $"'{row.Name}' is already in your library.",
                added ? ToastType.Success : ToastType.Info,
                scope: "radio-add");
        }
        finally
        {
            row.IsBusy = false;
        }
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e) => Close();
}