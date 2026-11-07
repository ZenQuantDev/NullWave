using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using NullWave.Helpers;
using NullWave.Models;
using NullWave.Services;
using NullWave.Services.Plugins;
using Serilog;

namespace NullWave.ViewModels.Settings;

public partial class PluginRowViewModel : ObservableObject, IDisposable
{
    private readonly IPlugin _plugin;
    private readonly Action<bool> _persistToggle;
    private CancellationTokenSource? _initCts;

    public string Name => _plugin.Name switch
    {
        "yt-dlp Downloader" => LocalizationService.Instance["Settings_Integrations_Plugin_YtDlp_Name"],
        "Last.fm" => LocalizationService.Instance["Settings_Integrations_Plugin_LastFm_Name"],
        "OpenWeather" => LocalizationService.Instance["Settings_Integrations_Plugin_OpenWeather_Name"],
        "Ollama Local AI" => LocalizationService.Instance["Settings_Integrations_Plugin_Ollama_Name"],
        _ => _plugin.Name
    };

    public string Description => _plugin.Name switch
    {
        "yt-dlp Downloader" => LocalizationService.Instance["Settings_Integrations_Plugin_YtDlp_Desc"],
        "Last.fm" => LocalizationService.Instance["Settings_Integrations_Plugin_LastFm_Desc"],
        "OpenWeather" => LocalizationService.Instance["Settings_Integrations_Plugin_OpenWeather_Desc"],
        "Ollama Local AI" => LocalizationService.Instance["Settings_Integrations_Plugin_Ollama_Desc"],
        _ => _plugin.Description
    };

    [ObservableProperty]
    private PluginState _state;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private bool _isBusy;

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
        LocalizationService.Instance.PropertyChanged += OnLocalizationChanged;
    }

    private void OnLocalizationChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Description));
    }

    public void Dispose()
    {
        LocalizationService.Instance.PropertyChanged -= OnLocalizationChanged;
        _initCts?.Cancel();
        _initCts?.Dispose();
    }

    // FIX: Replaced partial void OnIsEnabledChanged with an explicit method called 
    // from the View's Click event. This completely eliminates the Avalonia binding 
    // coercion storm that caused background threads to mutate the UI collection.
    public void UserToggled(bool newValue)
    {
        if (_plugin.IsEnabled == newValue) return;

        _plugin.IsEnabled = newValue;
        _persistToggle(newValue);
        IsEnabled = newValue; // Update UI property
        
        _initCts?.Cancel();
        _initCts?.Dispose();
        _initCts = new CancellationTokenSource();
        
        _ = ReinitializeAsync(_initCts.Token);
    }

    private async Task ReinitializeAsync(CancellationToken ct)
    {
        await Dispatcher.UIThread.InvokeAsync(() => IsBusy = true);

        try
        {
            var wasEnabled = _plugin.IsEnabled;
            
            // Run the actual plugin initialization
            await _plugin.InitializeAsync(ct); 
            
            if (ct.IsCancellationRequested) return;

            // Marshal all property changes back to the UI thread
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                State = _plugin.State;
                OnPropertyChanged(nameof(StatusDotBrush));
            });

            // Only notify if the user just enabled it
            if (!wasEnabled) return;

            var message = State switch
            {
                PluginState.Available => string.Format(LocalizationService.Instance["Settings_Integrations_Plugin_Connected"], Name),
                PluginState.Error     => string.Format(LocalizationService.Instance["Settings_Integrations_Plugin_Failed"], Name),
                PluginState.Unavailable => string.Format(LocalizationService.Instance["Settings_Integrations_Plugin_Unavailable"], Name),
                _ => null
            };

            if (message != null)
            {
                var type = State == PluginState.Available ? ToastType.Success : ToastType.Warning;
                Dispatcher.UIThread.Post(() => ToastService.Instance.Show(message, type));
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when the user toggles rapidly
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[PluginRowViewModel] Initialization failed for {Name}", Name);
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                await Dispatcher.UIThread.InvokeAsync(() => IsBusy = false);
            }
        }
    }
}