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
    // General & Downloads
    public string DownloadDirectory { get => _prefsService.Current.DownloadDirectory; set { _prefsService.Update(p => p.DownloadDirectory = value); OnPropertyChanged(); ScheduleSave(); } }
    public bool AutoFetchMetadata { get => _prefsService.Current.AutoFetchMetadata; set { _prefsService.Update(p => p.AutoFetchMetadata = value); OnPropertyChanged(); ScheduleSave(); } }
    public bool AutoPlayNext { get => _prefsService.Current.AutoPlayNext; set { _prefsService.Update(p => p.AutoPlayNext = value); OnPropertyChanged(); ScheduleSave(); } }
    public bool DownloadOnAdd { get => _prefsService.Current.DownloadOnAdd; set { _prefsService.Update(p => p.DownloadOnAdd = value); OnPropertyChanged(); ScheduleSave(); } }
    public bool ScrobbleToLastFm { get => _prefsService.Current.ScrobbleToLastFm; set { _prefsService.Update(p => p.ScrobbleToLastFm = value); OnPropertyChanged(); ScheduleSave(); } }
    public bool AutoCleanMetadata { get => _prefsService.Current.AutoCleanMetadata; set { _prefsService.Update(p => p.AutoCleanMetadata = value); OnPropertyChanged(); ScheduleSave(); } }
    public bool PreventDuplicateDownloads { get => _prefsService.Current.PreventDuplicateDownloads; set { _prefsService.Update(p => p.PreventDuplicateDownloads = value); OnPropertyChanged(); ScheduleSave(); } }
    public bool UseAria2c { get => _prefsService.Current.UseAria2c; set { _prefsService.Update(p => p.UseAria2c = value); OnPropertyChanged(); ScheduleSave(); } }
    public string YtDlpBrowserCookies { get => _prefsService.Current.YtDlpBrowserCookies; set { _prefsService.Update(p => p.YtDlpBrowserCookies = value); OnPropertyChanged(); ScheduleSave(); } }
    public string YtDlpProxy { get => _prefsService.Current.YtDlpProxy; set { _prefsService.Update(p => p.YtDlpProxy = value); OnPropertyChanged(); ScheduleSave(); } }
    public string YtDlpGeoProxy { get => _prefsService.Current.YtDlpGeoProxy; set { _prefsService.Update(p => p.YtDlpGeoProxy = value); OnPropertyChanged(); ScheduleSave(); } }
    
    public int MaxConcurrentDownloads
    {
        get => _prefsService.Current.MaxConcurrentDownloads;
        set { _prefsService.Update(p => p.MaxConcurrentDownloads = value); OnPropertyChanged(); ScheduleSave(); MaxConcurrentDownloadsChanged?.Invoke(value); }
    }
    
    public int SkipPenaltyWindowSeconds { get => _prefsService.Current.SkipPenaltyWindowSeconds; set { _prefsService.Update(p => p.SkipPenaltyWindowSeconds = value); OnPropertyChanged(); ScheduleSave(); } }
    public int SkipPenaltyCap { get => _prefsService.Current.SkipPenaltyCap; set { _prefsService.Update(p => p.SkipPenaltyCap = value); OnPropertyChanged(); ScheduleSave(); } }
    public bool EnableAutoBackups { get => _prefsService.Current.EnableAutoBackups; set { _prefsService.Update(p => p.EnableAutoBackups = value); OnPropertyChanged(); ScheduleSave(); } }
    public int BackupRetentionCount { get => _prefsService.Current.BackupRetentionCount; set { _prefsService.Update(p => p.BackupRetentionCount = value); OnPropertyChanged(); ScheduleSave(); } }
    public bool AutoResumeAudiobooks { get => _prefsService.Current.AutoResumeAudiobooks; set { _prefsService.Update(p => p.AutoResumeAudiobooks = value); OnPropertyChanged(); ScheduleSave(); } }
    public float AudiobookPlaybackRate { get => _prefsService.Current.AudiobookPlaybackRate; set { _prefsService.Update(p => p.AudiobookPlaybackRate = value); OnPropertyChanged(); ScheduleSave(); } }

    // Audio
    public string AudioQuality { get => _prefsService.Current.AudioQuality; set { _prefsService.Update(p => p.AudioQuality = value); OnPropertyChanged(); ScheduleSave(); } }
    public string AudioFormat { get => _prefsService.Current.AudioFormat; set { _prefsService.Update(p => p.AudioFormat = value); OnPropertyChanged(); ScheduleSave(); } }
    public bool FadeOnPauseEnabled { get => _prefsService.Current.FadeOnPauseEnabled; set { _prefsService.Update(p => p.FadeOnPauseEnabled = value); OnPropertyChanged(); ScheduleSave(); } }
    
    public int FadeOnPauseDurationMs
    {
        get => _prefsService.Current.FadeOnPauseDurationMs;
        set { _prefsService.Update(p => p.FadeOnPauseDurationMs = value); OnPropertyChanged(); OnPropertyChanged(nameof(FadeDurationDisplay)); ScheduleSave(); }
    }
    
    public bool CrossfadeEnabled { get => _prefsService.Current.CrossfadeEnabled; set { _prefsService.Update(p => p.CrossfadeEnabled = value); OnPropertyChanged(); ScheduleSave(); } }
    
    public int CrossfadeDurationSeconds
    {
        get => _prefsService.Current.CrossfadeDurationSeconds;
        set { _prefsService.Update(p => p.CrossfadeDurationSeconds = value); OnPropertyChanged(); OnPropertyChanged(nameof(CrossfadeDurationDisplay)); ScheduleSave(); }
    }
    
    public float ScrobbleThreshold
    {
        get => _prefsService.Current.ScrobbleThreshold;
        set { _prefsService.Update(p => p.ScrobbleThreshold = value); OnPropertyChanged(); OnPropertyChanged(nameof(ScrobbleThresholdDisplay)); ScheduleSave(); }
    }

    // Appearance & Layout
    public string ThemeMode
    {
        get => _prefsService.Current.ThemeMode;
        set { _prefsService.Update(p => p.ThemeMode = value); OnPropertyChanged(); ThemeService.Instance.ApplyThemeMode(value); ScheduleSave(); }
    }
    public string AccentColor
    {
        get => _prefsService.Current.AccentColor;
        set { _prefsService.Update(p => p.AccentColor = value); OnPropertyChanged(); ThemeService.Instance.ApplyAccent(value); ScheduleSave(); }
    }
    public string TrackRowStyle
    {
        get => _prefsService.Current.TrackRowStyle;
        set { _prefsService.Update(p => p.TrackRowStyle = value); OnPropertyChanged(); ThemeService.Instance.ApplyDensity(_prefsService.Current); ScheduleSave(); }
    }
    public string FontScale
    {
        get => _prefsService.Current.FontScale;
        set { _prefsService.Update(p => p.FontScale = value); OnPropertyChanged(); ThemeService.Instance.ApplyFontScale(value); ScheduleSave(); }
    }
    public bool CompactMode
    {
        get => _prefsService.Current.CompactMode;
        set { _prefsService.Update(p => p.CompactMode = value); OnPropertyChanged(); ThemeService.Instance.ApplyDensity(_prefsService.Current); ScheduleSave(); }
    }
    public string SidebarWidth
    {
        get => _prefsService.Current.SidebarWidth;
        set { _prefsService.Update(p => p.SidebarWidth = value); OnPropertyChanged(); ThemeService.Instance.ApplySidebarWidth(value); ScheduleSave(); }
    }
    public string ProfileFrameStyle
    {
        get => _prefsService.Current.ProfileFrameStyle;
        set { _prefsService.Update(p => p.ProfileFrameStyle = value); OnPropertyChanged(); ThemeService.Instance.ApplyProfileFrame(value); ScheduleSave(); }
    }

    // AI & Smart Features
    public string SelectedModel { get => _prefsService.Current.SelectedAIModel; set { _prefsService.Update(p => p.SelectedAIModel = value); OnPropertyChanged(); ScheduleSave(); } }
    public bool UseLocalAI { get => _prefsService.Current.UseLocalAI; set { _prefsService.Update(p => p.UseLocalAI = value); OnPropertyChanged(); ScheduleSave(); } }
    public double Latitude { get => _prefsService.Current.Latitude; set { _prefsService.Update(p => p.Latitude = value); OnPropertyChanged(); ScheduleSave(); } }
    public double Longitude { get => _prefsService.Current.Longitude; set { _prefsService.Update(p => p.Longitude = value); OnPropertyChanged(); ScheduleSave(); } }
    public bool AutoGenerateMoodPlaylist { get => _prefsService.Current.AutoGenerateMoodPlaylist; set { _prefsService.Update(p => p.AutoGenerateMoodPlaylist = value); OnPropertyChanged(); ScheduleSave(); } }
    public string MoodRefreshInterval { get => _prefsService.Current.MoodRefreshInterval; set { _prefsService.Update(p => p.MoodRefreshInterval = value); OnPropertyChanged(); ScheduleSave(); } }
    public string AIConfidenceThreshold { get => _prefsService.Current.AIConfidenceThreshold; set { _prefsService.Update(p => p.AIConfidenceThreshold = value); OnPropertyChanged(); ScheduleSave(); } }
    public string ExportFormat { get => _prefsService.Current.ExternalAIExportFormat; set { _prefsService.Update(p => p.ExternalAIExportFormat = value); OnPropertyChanged(); ScheduleSave(); } }
    
    public string BatteryModel
    {
        get => _prefsService.Current.BatteryModel;
        set { _prefsService.Update(p => p.BatteryModel = value); OnPropertyChanged(); ScheduleSave(); PowerModelsChanged?.Invoke(value, PerformanceModel, AutoPowerModelSwitch); }
    }
    public string PerformanceModel
    {
        get => _prefsService.Current.PerformanceModel;
        set { _prefsService.Update(p => p.PerformanceModel = value); OnPropertyChanged(); ScheduleSave(); PowerModelsChanged?.Invoke(BatteryModel, value, AutoPowerModelSwitch); }
    }
    public bool AutoPowerModelSwitch
    {
        get => _prefsService.Current.AutoPowerModelSwitch;
        set { _prefsService.Update(p => p.AutoPowerModelSwitch = value); OnPropertyChanged(); ScheduleSave(); PowerModelsChanged?.Invoke(BatteryModel, PerformanceModel, value); }
    }
    
    public bool AIFeaturesEnabled
    {
        get => _prefsService.Current.AIFeaturesEnabled;
        set
        {
            _prefsService.Update(p => p.AIFeaturesEnabled = value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(AIFeaturesControlsEnabled));
            ScheduleSave();
            AIFeaturesEnabledChanged?.Invoke(value);
        }
    }

    // Queue
    public int QueueAutoFillSize
    {
        get => _prefsService.Current.QueueAutoFillSize;
        set { _prefsService.Update(p => p.QueueAutoFillSize = value); OnPropertyChanged(); ScheduleSave(); }
    }
    public bool QueueManualInsertAtBlockEnd
    {
        get => _prefsService.Current.QueueManualInsertAtBlockEnd;
        set { _prefsService.Update(p => p.QueueManualInsertAtBlockEnd = value); OnPropertyChanged(); ScheduleSave(); }
    }

    // Logging
    public bool VerboseLogging
    {
        get => _prefsService.Current.VerboseLogging;
        set
        {
            _prefsService.Update(p => p.VerboseLogging = value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(LoggingModeLabel));
            ScheduleSave();
            Helpers.Logging.NullWaveLogConfig.LevelSwitch.MinimumLevel = value ? LogEventLevel.Debug : LogEventLevel.Information;
            Log.Information("[Settings] Logging mode changed to {Mode}", value ? "Advanced/Verbose" : "Default");
        }
    }

    // Localized Formatters
    public string LoggingModeLabel => string.Format(L("Settings_Dynamic_Log_CurrentMode"), VerboseLogging ? L("Settings_Dynamic_Log_Verbose") : L("Settings_Dynamic_Log_Default"));
    public string FadeDurationDisplay => $"{FadeOnPauseDurationMs} ms";
    public string CrossfadeDurationDisplay => $"{CrossfadeDurationSeconds} s";
    public string ScrobbleThresholdDisplay => string.Format(L("Settings_Dynamic_Scrobble_Threshold"), ScrobbleThreshold);
    public bool AIFeaturesControlsEnabled => AIFeaturesEnabled;
}