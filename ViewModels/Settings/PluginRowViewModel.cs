using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using NullWave.Models;
using NullWave.Services.Plugins;

namespace NullWave.ViewModels.Settings;

public partial class PluginRowViewModel : ObservableObject
{
    private readonly IPlugin _plugin;
    private readonly Action<bool> _persistToggle;

    public string Name => _plugin.Name;
    public string Description => _plugin.Description;

    [ObservableProperty]
    private PluginState _state;

    [ObservableProperty]
    private bool _isEnabled;

    public string StatusDotColor => State switch
    {
        PluginState.Available => "#4CAF50",
        PluginState.Loading   => "#FCD34D",
        PluginState.Error     => "#F44336",
        PluginState.Disabled  => "#6B7280",
        _                     => "#6B7280" // Unavailable
    };

    public PluginRowViewModel(IPlugin plugin, Action<bool> persistToggle)
    {
        _plugin = plugin;
        _persistToggle = persistToggle;
        _state = plugin.State;
        _isEnabled = plugin.IsEnabled;
    }

    partial void OnIsEnabledChanged(bool value)
    {
        _plugin.IsEnabled = value;
        _persistToggle(value);
        _ = ReinitializeAsync();
    }

    private async Task ReinitializeAsync()
    {
        await _plugin.InitializeAsync();
        State = _plugin.State;
        OnPropertyChanged(nameof(StatusDotColor));
    }
}