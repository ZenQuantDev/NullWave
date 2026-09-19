using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NullWave.Helpers;
using NullWave.Helpers.Logging;
using NullWave.Models;
using NullWave.Services;
using NullWave.Services.Plugins;
using NullWave.Services.Security;
using NullWave.Services.SmartSorting;
using NullWave.ViewModels.Settings;
using Serilog;
using Serilog.Events;

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
        AIServiceState.Running => (IBrush)Avalonia.Application.Current!.Resources["BrushGreen"]!,
        AIServiceState.Starting => (IBrush)Avalonia.Application.Current!.Resources["BrushAmber"]!,
        AIServiceState.Error => (IBrush)Avalonia.Application.Current!.Resources["BrushRed"]!,
        _ => (IBrush)Avalonia.Application.Current!.Resources["BrushTextMuted"]!
    };

    public string AIToggleButtonLabel => AiServiceState == AIServiceState.Running ? L("Settings_Dynamic_AI_Stop") : L("Settings_Dynamic_AI_Start");

    [RelayCommand]
    private void DetectHardware()
    {
        IsDetectingHardware = true;
        try
        {
            var detector = new HardwareDetector();
            var info = detector.Detect();
            HardwareInfo = string.Format(L("Settings_Dynamic_HW_Info"), info.CpuCores, info.RamGB, info.GpuType, info.GpuVramGB, info.RecommendedModel, info.RecommendationReason);
            var currentPrefs = _prefsService.Current;
            if (string.IsNullOrEmpty(currentPrefs.SelectedAIModel)) SelectedModel = info.RecommendedModel;
            else OnPropertyChanged(nameof(SelectedModel));
            
            var suggestedBattery = AIModelCatalog.SuggestBatteryModel(info.RamGB);
            var suggestedPerf = AIModelCatalog.SuggestPerformanceModel(info.RamGB, info.GpuVramGB, info.HasNvidia || info.HasAmd);
            
            if (string.IsNullOrEmpty(currentPrefs.BatteryModel)) BatteryModel = suggestedBattery;
            else OnPropertyChanged(nameof(BatteryModel));
            if (string.IsNullOrEmpty(currentPrefs.PerformanceModel)) PerformanceModel = suggestedPerf;
            else OnPropertyChanged(nameof(PerformanceModel));
            
            UpdatePowerState();
        }
        catch (Exception ex) { HardwareInfo = string.Format(L("Settings_Dynamic_HW_Failed"), ex.Message); }
        finally { IsDetectingHardware = false; }
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
            var progress = new Progress<double>(pct =>
            {
                ModelDownloadProgress = pct * 100;
                ModelDownloadStatus = string.Format(L("Settings_Dynamic_Model_DownloadingPct"), SelectedModel, pct);
            });
            await _localAI.DownloadModelAsync(SelectedModel, progress);
            ModelDownloadStatus = string.Format(L("Settings_Dynamic_Model_Success"), SelectedModel);
            await ToggleAIServiceAsync();
        }
        catch (Exception ex) { ModelDownloadStatus = string.Format(L("Settings_Dynamic_Model_Failed"), ex.Message); }
        finally { IsDownloadingModel = false; }
    }

    [RelayCommand]
    private async Task ToggleAIServiceAsync()
    {
        if (!AIFeaturesEnabled) { AiServiceState = AIServiceState.Stopped; return; }
        if (AiServiceState == AIServiceState.Running) { AiServiceState = AIServiceState.Stopped; return; }
        AiServiceState = AIServiceState.Starting;
        try
        {
            _localAI.CurrentModel = SelectedModel;
            bool ok = await _localAI.PingAsync();
            AiServiceState = ok ? AIServiceState.Running : AIServiceState.Error;
        }
        catch { AiServiceState = AIServiceState.Error; }
    }

    private async Task ProbeOllamaOnStartupAsync()
    {
        try
        {
            if (!AIFeaturesEnabled) { AiServiceState = AIServiceState.Stopped; return; }
            bool running = await _localAI.PingAsync();
            if (AiServiceState == AIServiceState.Stopped) AiServiceState = running ? AIServiceState.Running : AIServiceState.Stopped;
            if (_plugins.Get<OllamaAIProvider>() is { } ollama)
                ollama.State = running ? PluginState.Available : PluginState.Unavailable;
        }
        catch { }
    }

    public void StartAIHealthCheck()
    {
        _aiHealthTimer = new System.Threading.Timer(async _ => await HealthCheckTickAsync(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    private async Task HealthCheckTickAsync()
    {
        try
        {
            if (!AIFeaturesEnabled || AiServiceState == AIServiceState.Stopped) return;
            bool reachable = await _localAI.PingAsync();
            var newState = reachable ? AIServiceState.Running : AIServiceState.Error;
            if (AiServiceState != newState) AiServiceState = newState;
            if (_plugins.Get<OllamaAIProvider>() is { } ollama)
                ollama.State = reachable ? PluginState.Available : PluginState.Error;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[Settings] AI Health check error");
            AiServiceState = AIServiceState.Error;
            if (_plugins.Get<OllamaAIProvider>() is { } ollama) ollama.State = PluginState.Error;
        }
    }

    private void UpdatePowerState()
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                bool onBattery = true;
                const string sysfsPath = "/sys/class/power_supply";
                if (Directory.Exists(sysfsPath))
                {
                    foreach (var dir in Directory.GetDirectories(sysfsPath))
                    {
                        if (dir.Contains("AC") || dir.Contains("ADP") || dir.Contains("ACAD"))
                        {
                            var onlineFile = Path.Combine(dir, "online");
                            if (File.Exists(onlineFile) && File.ReadAllText(onlineFile).Trim() == "1") { onBattery = false; break; }
                        }
                    }
                }
                PowerStateLabel = onBattery ? L("Settings_Dynamic_Power_Battery") : L("Settings_Dynamic_Power_AC_Perf");
            }
            else if (OperatingSystem.IsWindows()) { PowerStateLabel = L("Settings_Dynamic_Power_AC_Connected"); }
            else { PowerStateLabel = L("Settings_Dynamic_Power_AC_Source"); }
        }
        catch { PowerStateLabel = L("Settings_Dynamic_Power_Unknown"); }
    }

    public void StopHealthCheck() { _aiHealthTimer?.Dispose(); _aiHealthTimer = null; }
    public void SetAIServiceState(AIServiceState state) => AiServiceState = state;
}