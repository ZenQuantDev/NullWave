using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Material.Icons;
using NullWave.Helpers;
using NullWave.Models;
using NullWave.Services;
using NullWave.Services.Plugins;
using Serilog;
using NullWave.Plugins.Contracts;

namespace NullWave.ViewModels.Settings;

public partial class PluginRowViewModel : ObservableObject, IDisposable
{
    private readonly IPlugin _plugin;
    private readonly Action<bool> _persistToggle;
    private CancellationTokenSource? _initCts;

    public PluginManifest Manifest { get; }

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

    public string Author => Manifest.Author;
    public string VersionLabel => Manifest.BuiltIn
        ? LocalizationService.Instance["Settings_Plugins_BuiltIn"]
        : Manifest.Version;
    public IReadOnlyList<string> KindTags => PluginUi.Tags(Manifest.Kinds);
    public MaterialIconKind PrimaryIcon => PluginUi.IconFor(Manifest.Kinds);

    private PluginState EffectiveState => !IsEnabled ? PluginState.Disabled : State;

    public string StatusText => EffectiveState switch
    {
        PluginState.Available => LocalizationService.Instance["Settings_Integrations_Status_Available"],
        PluginState.Loading => LocalizationService.Instance["Settings_Integrations_Status_Loading"],
        PluginState.Error => LocalizationService.Instance["Settings_Integrations_Status_Error"],
        PluginState.Disabled => LocalizationService.Instance["Settings_Integrations_Status_Disabled"],
        PluginState.Unavailable => LocalizationService.Instance["Settings_Plugins_NeedsSetup"],
        _ => LocalizationService.Instance["Settings_Integrations_Status_Unknown"]
    };

    public string? SetupPage => EffectiveState != PluginState.Unavailable
        ? null
        : Manifest.Id switch
        {
            "lastfm" or "openweather" => "ApiKeys",
            "yt-dlp" => "Updates",
            "ollama" => "SmartFeatures",
            _ => null
        };

    public bool NeedsAttention => IsEnabled && State == PluginState.Unavailable;

    [ObservableProperty]
    private PluginState _state;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private bool _isBusy;

    public IBrush StatusDotBrush => EffectiveState switch
    {
        PluginState.Available => (IBrush)Application.Current!.Resources["BrushGreen"]!,
        PluginState.Loading => (IBrush)Application.Current!.Resources["BrushAmber"]!,
        PluginState.Error => (IBrush)Application.Current!.Resources["BrushRed"]!,
        PluginState.Disabled => (IBrush)Application.Current!.Resources["BrushTextMuted"]!,
        _ => (IBrush)Application.Current!.Resources["BrushAmber"]!
    };

    public PluginRowViewModel(IPlugin plugin, Action<bool> persistToggle)
    {
        _plugin = plugin;
        _persistToggle = persistToggle;
        _state = plugin.State;
        _isEnabled = plugin.IsEnabled;
        Manifest = BuiltInPluginManifest.FromBuiltIn(plugin);
        LocalizationService.Instance.PropertyChanged += OnLocalizationChanged;
    }

    partial void OnStateChanged(PluginState value)
    {
        NotifyStatus();
    }

    partial void OnIsEnabledChanged(bool value)
    {
        NotifyStatus();
    }

    private void NotifyStatus()
    {
        OnPropertyChanged(nameof(StatusDotBrush));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(SetupPage));
        OnPropertyChanged(nameof(NeedsAttention));
    }

    public void RefreshFromPlugin()
    {
        State = _plugin.State;
        IsEnabled = _plugin.IsEnabled;
        NotifyStatus();
    }

    private void OnLocalizationChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(VersionLabel));
        OnPropertyChanged(nameof(KindTags));
        OnPropertyChanged(nameof(StatusText));
    }

    public void Dispose()
    {
        LocalizationService.Instance.PropertyChanged -= OnLocalizationChanged;
        _initCts?.Cancel();
        _initCts?.Dispose();
    }

    public bool Matches(string query, string category) =>
        PluginUi.Matches(Manifest, Name, Description, query, category);

    public void UserToggled(bool newValue)
    {
        if (_plugin.IsEnabled == newValue) return;

        _plugin.IsEnabled = newValue;
        _persistToggle(newValue);
        IsEnabled = newValue;
        NotifyStatus();

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

            await _plugin.InitializeAsync(null!, ct);

            if (ct.IsCancellationRequested) return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                State = _plugin.State;
                NotifyStatus();
            });

            if (!wasEnabled) return;

            var message = State switch
            {
                PluginState.Available => string.Format(LocalizationService.Instance["Settings_Integrations_Plugin_Connected"], Name),
                PluginState.Error => string.Format(LocalizationService.Instance["Settings_Integrations_Plugin_Failed"], Name),
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