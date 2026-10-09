using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NullWave.Services;
using NullWave.Services.SmartSorting;
using NullWave.Services.Plugins;
using Serilog;

namespace NullWave.ViewModels;

public partial class SettingsViewModel
{
    [ObservableProperty] private string _hardwareInfo = LocalizationService.Instance["Settings_Dynamic_HW_NotDetected"];
    [ObservableProperty] private bool _isDetectingHardware;
    [ObservableProperty] private string _powerStateLabel = LocalizationService.Instance["Settings_Dynamic_Power_Detecting"];
    [ObservableProperty] private bool _isDownloadingModel;
    [ObservableProperty] private double _modelDownloadProgress;
    [ObservableProperty] private string _modelDownloadStatus = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AIStatusLabel))]
    [NotifyPropertyChangedFor(nameof(AIStatusDescription))]
    [NotifyPropertyChangedFor(nameof(AIStatusDotBrush))]
    [NotifyPropertyChangedFor(nameof(AIToggleButtonLabel))]
    private AIServiceState _aiServiceState = AIServiceState.Stopped;

    public string AIStatusLabel => AiServiceState switch
    {
        AIServiceState.Running => L("Settings_Dynamic_AI_Active"),
        AIServiceState.Starting => L("Settings_Dynamic_AI_Starting"),
        AIServiceState.Error => L("Settings_Dynamic_AI_Error"),
        _ => L("Settings_Dynamic_AI_Stopped")
    };

    public string AIStatusDescription => AiServiceState switch
    {
        AIServiceState.Running => string.Format(L("Settings_Dynamic_AI_Desc_Ready"), SelectedModel),
        AIServiceState.Starting => L("Settings_Dynamic_AI_Desc_Loading"),
        AIServiceState.Error => L("Settings_Dynamic_AI_Desc_Error"),
        _ => L("Settings_Dynamic_AI_Desc_Stopped")
    };

    public IBrush AIStatusDotBrush => AiServiceState switch
    {
        AIServiceState.Running => (IBrush)Application.Current!.Resources["BrushGreen"]!,
        AIServiceState.Starting => (IBrush)Application.Current!.Resources["BrushAmber"]!,
        AIServiceState.Error => (IBrush)Application.Current!.Resources["BrushRed"]!,
        _ => (IBrush)Application.Current!.Resources["BrushTextMuted"]!
    };

    public string AIToggleButtonLabel => AiServiceState == AIServiceState.Running
        ? L("Settings_Dynamic_AI_Stop")
        : L("Settings_Dynamic_AI_Start");

    [RelayCommand]
    private async Task DetectHardwareAsync()
    {
        IsDetectingHardware = true;
        try
        {
            var info = await HardwareDetector.RefreshAsync();
            var recommendedModel = info.RecommendedModel ?? "none";
            HardwareInfo = string.Format(
                L("Settings_Dynamic_HW_Info"),
                info.CpuCores,
                info.RamGB,
                info.GpuType,
                info.GpuVramGB,
                recommendedModel,
                info.RecommendationReason);

            var currentPrefs = _prefsService.Current;
            if (string.IsNullOrEmpty(currentPrefs.SelectedAIModel))
                SelectedModel = info.RecommendedModel ?? "qwen2.5:0.5b";
            else
                OnPropertyChanged(nameof(SelectedModel));

            var batteryModel = AIModelCatalog.SuggestBatteryModel(info.RamGB);
            var performanceModel = AIModelCatalog.SuggestPerformanceModel(
                info.RamGB,
                info.GpuVramGB,
                info.HasNvidia || info.HasAmd,
                info.HasAvx,
                info.HasAvx2,
                info.IsArm64) ?? "qwen2.5:0.5b";

            if (string.IsNullOrEmpty(currentPrefs.BatteryModel)) BatteryModel = batteryModel;
            else OnPropertyChanged(nameof(BatteryModel));
            if (string.IsNullOrEmpty(currentPrefs.PerformanceModel)) PerformanceModel = performanceModel;
            else OnPropertyChanged(nameof(PerformanceModel));

            UpdatePowerState();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[Settings] Hardware detection failed");
            HardwareInfo = string.Format(L("Settings_Dynamic_HW_Failed"), ex.Message);
        }
        finally
        {
            IsDetectingHardware = false;
        }
    }

    [RelayCommand]
    private async Task DownloadModelAsync()
    {
        if (IsDownloadingModel) return;

        IsDownloadingModel = true;
        ModelDownloadProgress = 0;
        ModelDownloadStatus = string.Format(L("Settings_Dynamic_Model_Downloading"), SelectedModel);
        try
        {
            var progress = new Progress<double>(percentage =>
            {
                ModelDownloadProgress = percentage * 100;
                ModelDownloadStatus = string.Format(
                    L("Settings_Dynamic_Model_DownloadingPct"), SelectedModel, percentage);
            });
            await _localAI.DownloadModelAsync(SelectedModel, progress);
            ModelDownloadStatus = string.Format(L("Settings_Dynamic_Model_Success"), SelectedModel);
            await ToggleAIServiceAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[Settings] Model download failed");
            ModelDownloadStatus = string.Format(L("Settings_Dynamic_Model_Failed"), ex.Message);
        }
        finally
        {
            IsDownloadingModel = false;
        }
    }

    [RelayCommand]
    private async Task ToggleAIServiceAsync()
    {
        if (!AIFeaturesEnabled)
        {
            AiServiceState = AIServiceState.Stopped;
            return;
        }

        if (AiServiceState == AIServiceState.Running)
        {
            AiServiceState = AIServiceState.Stopped;
            return;
        }

        AiServiceState = AIServiceState.Starting;
        try
        {
            _localAI.CurrentModel = SelectedModel;
            var reachable = await _localAI.PingAsync();
            AiServiceState = reachable ? AIServiceState.Running : AIServiceState.Error;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[Settings] Failed to start local AI");
            AiServiceState = AIServiceState.Error;
        }
    }

    private async Task ProbeOllamaOnStartupAsync()
    {
        try
        {
            if (!AIFeaturesEnabled)
            {
                AiServiceState = AIServiceState.Stopped;
                return;
            }

            var reachable = await _localAI.PingAsync();
            if (AiServiceState == AIServiceState.Stopped)
                AiServiceState = reachable ? AIServiceState.Running : AIServiceState.Stopped;

            if (_plugins.Get<OllamaAIProvider>() is { } ollama)
                ollama.State = reachable ? PluginState.Available : PluginState.Unavailable;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[Settings] Ollama startup probe failed");
        }
    }

    public void StartAIHealthCheck()
    {
        StopHealthCheck();
        _aiHealthTimer = new System.Threading.Timer(
            async _ => await HealthCheckTickAsync(),
            null,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(30));
    }

    private async Task HealthCheckTickAsync()
    {
        try
        {
            if (!AIFeaturesEnabled || AiServiceState == AIServiceState.Stopped) return;

            var reachable = await _localAI.PingAsync();
            var newState = reachable ? AIServiceState.Running : AIServiceState.Error;
            Dispatcher.UIThread.Post(() => AiServiceState = newState);

            if (_plugins.Get<OllamaAIProvider>() is { } ollama)
                ollama.State = reachable ? PluginState.Available : PluginState.Error;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[Settings] AI health check failed");
            Dispatcher.UIThread.Post(() => AiServiceState = AIServiceState.Error);
            if (_plugins.Get<OllamaAIProvider>() is { } ollama)
                ollama.State = PluginState.Error;
        }
    }

    private void UpdatePowerState()
    {
        try
        {
            var state = PowerStateService.ReadPowerState();
            PowerStateLabel = state == PowerState.Battery
                ? L("Settings_Dynamic_Power_Battery")
                : L("Settings_Dynamic_Power_AC_Connected");
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[Settings] Power state detection failed");
            PowerStateLabel = L("Settings_Dynamic_Power_Unknown");
        }
    }

    public void StopHealthCheck()
    {
        _aiHealthTimer?.Dispose();
        _aiHealthTimer = null;
    }

    public void SetAIServiceState(AIServiceState state)
    {
        if (Dispatcher.UIThread.CheckAccess()) AiServiceState = state;
        else Dispatcher.UIThread.Post(() => AiServiceState = state);
    }
}