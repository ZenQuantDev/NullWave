using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NullWave.Models;
using NullWave.Services;
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

    public IBrush StatusDotBrush => State switch
    {
        PluginState.Available => (IBrush)Application.Current!.Resources["BrushGreen"]!,
        PluginState.Loading   => (IBrush)Application.Current!.Resources["BrushAmber"]!,
        PluginState.Error     => (IBrush)Application.Current!.Resources["BrushRed"]!,
        _                     => (IBrush)Application.Current!.Resources["BrushTextMuted"]!
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
        var wasEnabled = _plugin.IsEnabled;
        await _plugin.InitializeAsync();
        State = _plugin.State;
        OnPropertyChanged(nameof(StatusDotBrush));

        // Only notify if the user just enabled it (disabling is self-evident by the gray dot)
        if (!wasEnabled) return;

        var message = State switch
        {
            PluginState.Available => $"{Name} connected successfully.",
            PluginState.Error     => $"{Name} failed to connect - check configuration.",
            PluginState.Unavailable => $"{Name} is unavailable right now.",
            _ => null
        };

        if (message != null)
        {
            var type = State == PluginState.Available ? ToastType.Success : ToastType.Warning;
            Dispatcher.UIThread.Post(() => ToastService.Instance.Show(message, type));
        }
    }
}