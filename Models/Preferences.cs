using System;
using System.Collections.Generic;

namespace NullWave.Models;

public class Preferences
{
    // Audio
    public string AudioQuality { get; set; } = "best";
    public string AudioFormat { get; set; } = "mp3";
    public string DownloadDirectory { get; set; } = string.Empty;

    // Behavior
    public bool AutoFetchMetadata { get; set; } = true;
    public bool AutoPlayNext { get; set; } = true;
    public bool DownloadOnAdd { get; set; } = true;
    public bool ScrobbleToLastFm { get; set; } = true;

    // Appearance
    public string AccentColor    { get; set; } = "Purple";
    public string TrackRowStyle  { get; set; } = "Comfortable";
    public string FontScale      { get; set; } = "Medium";
    public bool   CompactMode    { get; set; } = false;
    public string SidebarWidth   { get; set; } = "Normal";

    // Smart Sorting 
    public string SelectedAIModel { get; set; } = "qwen2.5:7b";
    public bool   UseLocalAI      { get; set; } = true;
    public double Latitude        { get; set; } = 0.0;   // 0 = not set yet
    public double Longitude       { get; set; } = 0.0;
    
    // New Smart Features config
    public bool   AutoGenerateMoodPlaylist { get; set; } = false;
    public string MoodRefreshInterval      { get; set; } = "Never";
    public string AIConfidenceThreshold    { get; set; } = "70%";

    // External AI Export Format
    public string ExternalAIExportFormat { get; set; } = "txt";

    public float ScrobbleThreshold { get; set; } = 0.50f;
    public int SkipPenaltyWindowSeconds { get; set; } = 15;
    public int SkipPenaltyCap { get; set; } = 3;
    public int MaxConcurrentDownloads { get; set; } = 2;

    // Model to use when running on battery power (prefer small/fast)
    public string BatteryModel { get; set; } = "qwen2.5:3b";
    // Model to use when plugged in / GPU available (prefer quality)
    public string PerformanceModel { get; set; } = "qwen2.5:7b";
    // Whether to automatically switch models based on power state
    public bool AutoPowerModelSwitch { get; set; } = false;

    // Master AI Toggle
    public bool AIFeaturesEnabled { get; set; } = true;

    // Playback Transitions
    public bool FadeOnPauseEnabled { get; set; } = true;
    public int FadeOnPauseDurationMs { get; set; } = 300;
    public bool CrossfadeEnabled { get; set; } = true;
    public int CrossfadeDurationSeconds { get; set; } = 5;
}