using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using NullWave.Helpers;
using NullWave.Models;
using NullWave.Services;

namespace NullWave.Views.Dialogs;

public partial class RadioMoodDialog : Window
{
    private readonly List<RadioChannel> _channels;
    public RadioMoodDialog() : this(new List<RadioChannel>()) { }

    public RadioMoodDialog(IReadOnlyList<RadioChannel> channels)
    {
        InitializeComponent();
        _channels = channels.Select(c => new RadioChannel(c.Name, c.StreamUrl)).ToList();
        ChannelList.ItemsSource = _channels;
    }

    private async void OnFindClicked(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not RadioChannel ch) return;
        var resolved = await RadioStreamResolver.ResolveAsync(ch.Name);
        if (resolved != null) ch.StreamUrl = resolved;
        else ToastService.Instance.Show(
            string.Format(LocalizationService.Instance["Radio_Resolved_Failed"], ch.Name), ToastType.Warning);
        ChannelList.ItemsSource = null; ChannelList.ItemsSource = _channels;
    }

    private void OnAddClicked(object? sender, RoutedEventArgs e) =>
        Close(_channels.Where(c => !string.IsNullOrWhiteSpace(c.StreamUrl)).ToList());
}